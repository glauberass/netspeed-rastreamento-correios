"""
Lê tensores reais (PNG 128x32 gerados pelo C#) com o modelo e imprime leitura + confiança.
Serve para pré-rotular amostras (eu só corrijo o que estiver errado) e para medir o acerto.

    python prever.py modelo_v1.pt dados/tensores            # imprime leituras
    python prever.py modelo_v1.pt dados/tensores dados/reais.csv   # compara com os rótulos
"""
import csv
import sys
from pathlib import Path

import numpy as np
import torch
from PIL import Image

from modelo import CrnnCaptcha, decodificar


def main() -> None:
    pesos, pasta = sys.argv[1], Path(sys.argv[2])
    modelo = CrnnCaptcha()
    modelo.load_state_dict(torch.load(pesos))
    modelo.eval()

    rotulos = {}
    if len(sys.argv) > 3:
        with open(sys.argv[3], encoding="utf-8") as f:
            rotulos = {n: t.strip().lower() for n, t in csv.reader(f)}

    arquivos = sorted(pasta.glob("*.png"))
    if rotulos:
        arquivos = [a for a in arquivos if a.name in rotulos]

    ok = ok_car = total_car = 0
    with torch.no_grad():
        for arq in arquivos:
            x = torch.from_numpy(np.asarray(Image.open(arq).convert("L"), dtype=np.float32) / 255.0)[None, None]
            texto, conf = decodificar(modelo(x))[0]
            linha = f"{arq.stem:12} {texto:10} {conf:5.0%}"
            if arq.name in rotulos:
                real = rotulos[arq.name]
                ok += texto == real
                ok_car += sum(a == b for a, b in zip(texto, real))
                total_car += len(real)
                linha += f"   real={real:10} {'OK' if texto == real else 'x'}"
            print(linha)

    if rotulos:
        print(f"\nacerto do texto inteiro: {ok}/{len(arquivos)} = {ok / len(arquivos):.1%}; caracteres: {ok_car / total_car:.1%}")


if __name__ == "__main__":
    main()
