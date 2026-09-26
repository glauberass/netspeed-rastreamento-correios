"""
Compara modelos no MESMO conjunto de validação (a fatia de amostras reais que o treino de ajuste
fino deixou de fora — mesma semente e proporção de treinar.py).

    python comparar.py dados/reais.csv dados/tensores modelo_v1.pt modelo_v2.pt
"""
import random
import sys

import numpy as np
import torch

from modelo import CrnnCaptcha
from treinar import avaliar, carregar_reais


def main() -> None:
    csv_path, pasta, *modelos = sys.argv[1:]
    xs, ts = carregar_reais(csv_path, pasta)
    idx = list(range(len(xs)))
    random.Random(42).shuffle(idx)
    n_val = int(len(idx) * 0.2)
    val = idx[:n_val]
    vx, vt = xs[val], [ts[i] for i in val]

    print(f"validação: {len(val)} amostras reais (fora do treino do ajuste fino)")
    for caminho in modelos:
        m = CrnnCaptcha()
        m.load_state_dict(torch.load(caminho))
        acerto, acerto_car = avaliar(m, vx, vt)
        print(f"  {caminho:40} texto {acerto:.1%}  caractere {acerto_car:.1%}")


if __name__ == "__main__":
    main()
