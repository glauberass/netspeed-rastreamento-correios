"""
Exporta o modelo treinado para ONNX e confere que a saída do ONNX Runtime bate com a do PyTorch.

    python exportar_onnx.py modelo_v2.pt ../../modelos/captcha-crnn.onnx
"""
import sys

import numpy as np
import onnxruntime as ort
import torch

from modelo import CrnnCaptcha


def main(pesos: str, saida: str) -> None:
    modelo = CrnnCaptcha()
    modelo.load_state_dict(torch.load(pesos))
    modelo.eval()

    exemplo = torch.rand(1, 1, 32, 128)
    torch.onnx.export(
        modelo,
        exemplo,
        saida,
        input_names=["imagem"],
        output_names=["logits"],
        dynamic_axes={"imagem": {0: "lote"}, "logits": {1: "lote"}},
        opset_version=17,
        dynamo=False,
    )

    sessao = ort.InferenceSession(saida)
    lote = torch.rand(4, 1, 32, 128)
    esperado = modelo(lote).detach().numpy()
    obtido = sessao.run(None, {"imagem": lote.numpy()})[0]
    diferenca = float(np.abs(esperado - obtido).max())
    print(f"exportado em {saida}; diferença máxima PyTorch x ONNX: {diferenca:.2e}")
    assert diferenca < 1e-3, "ONNX diverge do PyTorch"


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
