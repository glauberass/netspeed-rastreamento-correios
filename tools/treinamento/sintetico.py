"""
Gerador de CAPTCHAs sintéticos, no domínio da MÁSCARA LIMPA.

A rede não vê a imagem original do portal (com linhas e pontos): vê o que sobra depois do
pré-processamento por cor (CaptchaImagePreprocessor, em C#) — só as letras, em preto e branco.
Por isso o gerador imita a *máscara*: letras deformadas do Securimage + os defeitos que a limpeza
deixa (falhas onde uma linha cruzou a letra, pequenos borrões que sobraram).

Fonte: o Securimage usa uma fonte sans bold (AHGBold). Não a temos, então usamos fontes bold do
Windows parecidas (Arial Bold/Black etc.) e compensamos com deformação aleatória forte; o
ajuste fino com amostras reais rotuladas fecha a diferença.
"""
from __future__ import annotations

import math
import os
import random

import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy.ndimage import binary_dilation, binary_erosion, binary_fill_holes, map_coordinates

ALFABETO = "abcdefghijklmnopqrstuvwxyz0123456789"

# Pesos de sorteio: dígitos 0 e 1 são raros/ausentes no Securimage do portal (ambíguos com o/l).
PESOS = {c: 1.0 for c in ALFABETO}
PESOS["0"] = 0.15
PESOS["1"] = 0.15

FONTES = [
    r"C:\Windows\Fonts\arialbd.ttf",
    r"C:\Windows\Fonts\ariblk.ttf",
    r"C:\Windows\Fonts\arial.ttf",
    r"C:\Windows\Fonts\ARIALNB.TTF",
    r"C:\Windows\Fonts\calibrib.ttf",
]
FONTES = [f for f in FONTES if os.path.exists(f)]

LARGURA, ALTURA = 215, 80


def _sortear_texto(rng: random.Random) -> str:
    n = rng.choices([4, 5, 6], weights=[0.25, 0.5, 0.25])[0]
    chars = list(PESOS)
    pesos = [PESOS[c] for c in chars]
    return "".join(rng.choices(chars, weights=pesos, k=n))


def _glifo(ch: str, fonte_path: str, tamanho: int, rng: random.Random) -> tuple[Image.Image, int]:
    """Devolve (glifo recortado, distância da linha de base ao topo do recorte)."""
    fonte = ImageFont.truetype(fonte_path, tamanho)
    pad = tamanho // 2
    linha_base = pad + tamanho  # a linha de base fica fixa; assim o alinhamento do texto é realista
    img = Image.new("L", (tamanho * 2, tamanho * 2 + pad), 0)
    ImageDraw.Draw(img).text((pad, linha_base), ch, font=fonte, fill=255, anchor="ls")

    # Cisalhamento horizontal + rotação, como a perturbação do Securimage.
    shear = rng.uniform(-0.2, 0.2)
    img = img.transform(img.size, Image.AFFINE, (1, shear, -shear * img.size[1] / 2, 0, 1, 0), Image.BILINEAR)
    img = img.rotate(rng.gauss(0, 5), resample=Image.BILINEAR, expand=False)

    # Escala anisotrópica leve.
    sx, sy = rng.uniform(0.9, 1.12), rng.uniform(0.9, 1.12)
    img = img.resize((max(4, int(img.size[0] * sx)), max(4, int(img.size[1] * sy))), Image.BILINEAR)

    # A escala anisotrópica acima muda a altura da linha de base na mesma proporção.
    linha_base = int(linha_base * sy)
    caixa = img.point(lambda v: 255 if v > 60 else 0).getbbox()
    if caixa:
        img = img.crop(caixa)
        linha_base -= caixa[1]

    # Às vezes a limpeza preenche o miolo da letra (o, a, 6...): imita isso.
    if rng.random() < 0.10:
        arr = binary_fill_holes(np.asarray(img) > 90).astype(np.uint8) * 255
        img = Image.fromarray(arr)
    return img, linha_base


def _onda(arr: np.ndarray, rng: random.Random) -> np.ndarray:
    """Distorção senoidal da imagem inteira (a 'perturbação' do Securimage)."""
    h, w = arr.shape
    amp_x, amp_y = rng.uniform(0.2, 1.0), rng.uniform(0.2, 1.4)
    per_x, per_y = rng.uniform(25, 70), rng.uniform(25, 70)
    fx, fy = rng.uniform(0, math.tau), rng.uniform(0, math.tau)
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    dx = amp_x * np.sin(yy / per_y * math.tau + fy)
    dy = amp_y * np.sin(xx / per_x * math.tau + fx)
    return map_coordinates(arr, [yy + dy, xx + dx], order=1, mode="constant", cval=0)


def _cortes_de_linha(arr: np.ndarray, rng: random.Random) -> np.ndarray:
    """Simula linhas que atravessaram as letras e abriram falhas (a limpeza remove esses pixels)."""
    img = Image.fromarray(arr)
    d = ImageDraw.Draw(img)
    for _ in range(rng.choice([0, 1, 2, 3, 4])):
        x0, y0 = rng.uniform(0, LARGURA), rng.uniform(15, ALTURA - 5)
        ang = rng.uniform(-0.5, 0.5)
        comp = rng.uniform(40, 200)
        x1, y1 = x0 + comp * math.cos(ang), y0 + comp * math.sin(ang)
        d.line([(x0, y0), (x1, y1)], fill=0, width=rng.choice([1, 1, 2, 3]))
    return np.array(img)


def _espessura(arr: np.ndarray, rng: random.Random) -> np.ndarray:
    """Varia a espessura do traço (a limpeza engrossa/afina conforme as linhas cruzadas)."""
    r = rng.random()
    mask = arr > 0
    if r < 0.12:
        mask = binary_dilation(mask, iterations=1)
    elif r < 0.20:
        mask = binary_erosion(mask, iterations=1)
    return mask.astype(np.uint8) * 255


def _borroes(arr: np.ndarray, rng: random.Random) -> np.ndarray:
    """Pequenos restos que a limpeza não removeu (pontos maiores que o limiar de área)."""
    out = arr.copy()
    for _ in range(rng.choice([0, 0, 0, 1, 2])):
        x, y = rng.randrange(0, LARGURA - 5), rng.randrange(0, ALTURA - 5)
        out[y : y + rng.randint(2, 4), x : x + rng.randint(2, 4)] = 255
    return out


def gerar_mascara(rng: random.Random, texto: str | None = None) -> tuple[np.ndarray, str]:
    """Devolve (máscara 0/255 de LARGURA x ALTURA, texto)."""
    texto = texto or _sortear_texto(rng)
    fonte = rng.choice(FONTES)

    canvas = np.zeros((ALTURA, LARGURA), dtype=np.uint8)
    x = rng.randint(5, 40)
    base = rng.randint(44, 56)  # linha de base comum do texto

    for ch in texto:
        fator = min(1.2, max(0.87, math.exp(rng.gauss(0, 0.08))))
        g, lb = _glifo(ch, fonte, max(16, int(rng.uniform(36, 46) * fator)), rng)
        gw, gh = g.size
        y = base + rng.randint(-5, 5) - lb
        y = max(0, min(ALTURA - gh, y))
        xi = min(x, LARGURA - gw)
        recorte = np.array(g)
        area = canvas[y : y + gh, xi : xi + gw]
        area[:] = np.maximum(area, recorte[: area.shape[0], : area.shape[1]])
        # Avança com sobreposição aleatória (letras coladas são comuns no portal).
        x += int(gw * rng.uniform(0.88, 1.06))

    canvas = (canvas > 90).astype(np.uint8) * 255
    canvas = _onda(canvas.astype(np.float32), rng)
    canvas = (canvas > 110).astype(np.uint8) * 255
    canvas = _espessura(canvas, rng)
    canvas = _cortes_de_linha(canvas, rng)
    canvas = _borroes(canvas, rng)
    return canvas, texto


def mascara_para_tensor(mascara: np.ndarray, largura: int = 128, altura: int = 32) -> np.ndarray:
    """Igual ao ParaTensor do C#: recorta no retângulo das letras e reduz por média de área."""
    ys, xs = np.nonzero(mascara)
    if len(xs) == 0:
        return np.zeros((altura, largura), dtype=np.float32)
    recorte = mascara[ys.min() : ys.max() + 1, xs.min() : xs.max() + 1]
    img = Image.fromarray(recorte).resize((largura, altura), Image.BOX if recorte.shape[1] > largura else Image.BILINEAR)
    return np.asarray(img, dtype=np.float32) / 255.0


def gerar_lote(n: int, semente: int) -> tuple[np.ndarray, list[str]]:
    rng = random.Random(semente)
    # uint8 (0..255): 4x menos memória ao trafegar entre processos; o treino divide por 255.
    xs = np.zeros((n, 32, 128), dtype=np.uint8)
    textos = []
    for i in range(n):
        m, t = gerar_mascara(rng)
        xs[i] = np.rint(mascara_para_tensor(m) * 255).astype(np.uint8)
        textos.append(t)
    return xs, textos


if __name__ == "__main__":
    # Amostra visual: python sintetico.py saida.png
    import sys

    rng = random.Random(1)
    linhas = []
    for _ in range(8):
        m, t = gerar_mascara(rng)
        linhas.append(255 - m)
        print(t)
    Image.fromarray(np.concatenate(linhas, axis=0)).save(sys.argv[1] if len(sys.argv) > 1 else "amostra_sintetica.png")
