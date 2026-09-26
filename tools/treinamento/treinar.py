"""
Treina a CRNN.

Fase 1 (só sintético):        python treinar.py --sinteticos 60000 --epocas 25 --saida modelo_v1.pt
Fase 2 (ajuste fino, real):   python treinar.py --sinteticos 40000 --epocas 25 --reais dados/reais.csv \
                                    --reais-dir dados/tensores --inicio modelo_v1.pt --saida modelo_v2.pt

reais.csv tem duas colunas: nome_do_arquivo,texto. Os tensores reais (PNG 128x32) são gerados pelo
C# (NetSpeed.CaptchaCalibrador tensor ...), garantindo que treino e inferência usem o MESMO
pré-processamento. Parte das amostras reais fica de fora do treino para medir o acerto de verdade.
"""
import os

# Cada processo do pool importa numpy/OpenBLAS: sem limitar, N processos x M threads estouram a memória.
for _v in ("OPENBLAS_NUM_THREADS", "OMP_NUM_THREADS", "MKL_NUM_THREADS"):
    os.environ.setdefault(_v, "1")

import argparse
import csv
import multiprocessing as mp
import random
import time
from pathlib import Path

import numpy as np
import torch
import torch.nn.functional as F
from PIL import Image

import sintetico
from modelo import ALFABETO, CLASSES, CrnnCaptcha, decodificar

torch.set_num_threads(8)


def _gerar(args):
    n, semente = args
    return sintetico.gerar_lote(n, semente)


def gerar_sinteticos(total: int, semente: int):
    procs = max(1, mp.cpu_count() - 1)
    parte = 500
    n_lotes = (total + parte - 1) // parte
    with mp.Pool(procs) as pool:
        lotes = pool.map(_gerar, [(parte, semente + i * 7919) for i in range(n_lotes)])
    xs = np.concatenate([l[0] for l in lotes])[:total].astype(np.float32) / 255.0
    ts = [t for l in lotes for t in l[1]][:total]
    return xs, ts


def carregar_reais(csv_path: str, diretorio: str):
    xs, ts = [], []
    with open(csv_path, encoding="utf-8") as f:
        for nome, texto in csv.reader(f):
            texto = texto.strip().lower()
            arq = Path(diretorio) / nome
            if not arq.exists() or not texto or any(c not in ALFABETO for c in texto):
                continue
            xs.append(np.asarray(Image.open(arq).convert("L"), dtype=np.float32) / 255.0)
            ts.append(texto)
    return np.stack(xs), ts


def aumentar(x: torch.Tensor) -> torch.Tensor:
    """Pequenas translações/escalas por lote: robustez ao recorte e à reamostragem do C#."""
    n = x.shape[0]
    theta = torch.zeros(n, 2, 3)
    esc = 1 + (torch.rand(n) - 0.5) * 0.12
    theta[:, 0, 0] = esc
    theta[:, 1, 1] = 1 + (torch.rand(n) - 0.5) * 0.12
    theta[:, 0, 2] = (torch.rand(n) - 0.5) * 0.06
    theta[:, 1, 2] = (torch.rand(n) - 0.5) * 0.10
    grade = F.affine_grid(theta, x.shape, align_corners=False)
    return F.grid_sample(x, grade, align_corners=False, padding_mode="zeros")


def alvos(textos):
    comprimentos = torch.tensor([len(t) for t in textos], dtype=torch.long)
    seq = torch.tensor([ALFABETO.index(c) + 1 for t in textos for c in t], dtype=torch.long)
    return seq, comprimentos


def avaliar(modelo, xs, ts, lote=256):
    modelo.eval()
    ok = ok_car = total_car = 0
    with torch.no_grad():
        for i in range(0, len(xs), lote):
            x = torch.from_numpy(xs[i : i + lote]).unsqueeze(1)
            for (pred, _), real in zip(decodificar(modelo(x)), ts[i : i + lote]):
                ok += pred == real
                ok_car += sum(a == b for a, b in zip(pred, real))
                total_car += len(real)
    return ok / len(xs), ok_car / max(1, total_car)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--sinteticos", type=int, default=60000)
    ap.add_argument("--epocas", type=int, default=25)
    ap.add_argument("--lote", type=int, default=128)
    ap.add_argument("--lr", type=float, default=2e-3)
    ap.add_argument("--reais")
    ap.add_argument("--reais-dir", default="dados/tensores")
    ap.add_argument("--reais-peso", type=int, default=6, help="quantas vezes repetir cada amostra real por época")
    ap.add_argument("--validacao", type=float, default=0.2)
    ap.add_argument("--inicio")
    ap.add_argument("--saida", default="modelo.pt")
    ap.add_argument("--semente", type=int, default=1)
    a = ap.parse_args()

    random.seed(a.semente)
    torch.manual_seed(a.semente)

    t0 = time.time()
    xs, ts = gerar_sinteticos(a.sinteticos, a.semente * 1000)
    print(f"{len(xs)} sintéticos em {time.time() - t0:.0f}s")

    val_xs = val_ts = None
    if a.reais:
        rx, rt = carregar_reais(a.reais, a.reais_dir)
        idx = list(range(len(rx)))
        random.Random(42).shuffle(idx)
        n_val = int(len(idx) * a.validacao)  # 0 = usa tudo no treino (modelo final)
        val, trn = idx[:n_val], idx[n_val:]
        if val:
            val_xs, val_ts = rx[val], [rt[i] for i in val]
        xs = np.concatenate([xs] + [rx[trn]] * a.reais_peso)
        ts = ts + [rt[i] for i in trn] * a.reais_peso
        print(f"reais: {len(trn)} treino x{a.reais_peso}, {len(val)} validação")

    modelo = CrnnCaptcha()
    if a.inicio:
        modelo.load_state_dict(torch.load(a.inicio))
    opt = torch.optim.AdamW(modelo.parameters(), lr=a.lr, weight_decay=1e-4)
    passos = a.epocas * ((len(xs) + a.lote - 1) // a.lote)
    sched = torch.optim.lr_scheduler.OneCycleLR(opt, max_lr=a.lr, total_steps=passos)
    ctc = torch.nn.CTCLoss(blank=0, zero_infinity=True)

    melhor = -1.0
    sint_val_xs, sint_val_ts = gerar_sinteticos(2000, 999_000)
    for ep in range(1, a.epocas + 1):
        modelo.train()
        perm = np.random.permutation(len(xs))
        soma = 0.0
        for i in range(0, len(perm), a.lote):
            b = perm[i : i + a.lote]
            x = aumentar(torch.from_numpy(xs[b]).unsqueeze(1))
            seq, comp = alvos([ts[j] for j in b])
            logits = modelo(x)
            loss = ctc(logits.log_softmax(-1), seq, torch.full((len(b),), logits.shape[0], dtype=torch.long), comp)
            opt.zero_grad()
            loss.backward()
            torch.nn.utils.clip_grad_norm_(modelo.parameters(), 5.0)
            opt.step()
            sched.step()
            soma += float(loss.detach()) * len(b)
            if (i // a.lote) % 50 == 0:
                print(f"  época {ep} lote {i // a.lote}/{len(perm) // a.lote}  loss {float(loss.detach()):.3f}  ({time.time() - t0:.0f}s)", flush=True)

        msg = f"época {ep:2d}/{a.epocas}  loss {soma / len(xs):.3f}"
        if val_xs is not None:
            acerto, acerto_car = avaliar(modelo, val_xs, val_ts)
            msg += f"  | real: texto {acerto:.1%} caractere {acerto_car:.1%}"
            if acerto >= melhor:
                melhor = acerto
                torch.save(modelo.state_dict(), a.saida)
        else:
            acerto, acerto_car = avaliar(modelo, sint_val_xs, sint_val_ts)
            msg += f"  | sintético: texto {acerto:.1%} caractere {acerto_car:.1%}"
            torch.save(modelo.state_dict(), a.saida)
        print(msg, f"({time.time() - t0:.0f}s)", flush=True)


if __name__ == "__main__":
    main()
