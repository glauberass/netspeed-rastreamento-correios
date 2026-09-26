"""
CRNN pequena para o CAPTCHA do portal: convoluções -> BiGRU -> CTC.

Entrada : [N, 1, 32, 128]  (cobertura de tinta 0..1; o mesmo tensor do CaptchaImagePreprocessor.ParaTensor)
Saída   : [T=32, N, C]     (logits por passo de tempo; classe 0 é o "branco" do CTC)

Cerca de 0,9 M de parâmetros: roda em milissegundos na CPU e o .onnx tem ~3 MB.
"""
import torch
import torch.nn as nn

ALFABETO = "abcdefghijklmnopqrstuvwxyz0123456789"
CLASSES = len(ALFABETO) + 1  # +1 = branco do CTC (índice 0)


def _bloco(entrada: int, saida: int) -> nn.Sequential:
    return nn.Sequential(
        nn.Conv2d(entrada, saida, 3, padding=1, bias=False),
        nn.BatchNorm2d(saida),
        nn.ReLU(inplace=True),
    )


class CrnnCaptcha(nn.Module):
    def __init__(self) -> None:
        super().__init__()
        self.cnn = nn.Sequential(
            _bloco(1, 24), nn.MaxPool2d(2, 2),          # 16 x 64
            _bloco(24, 48), nn.MaxPool2d(2, 2),         # 8 x 32
            _bloco(48, 96), _bloco(96, 96),
            nn.MaxPool2d((2, 1), (2, 1)),               # 4 x 32
            _bloco(96, 128), nn.MaxPool2d((4, 1), (4, 1)),  # 1 x 32
            nn.Dropout(0.15),
        )
        self.rnn = nn.GRU(128, 64, num_layers=2, bidirectional=True, batch_first=True, dropout=0.15)
        self.saida = nn.Linear(128, CLASSES)

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        f = self.cnn(x)                    # [N, 128, 1, 32]
        f = f.squeeze(2).permute(0, 2, 1)  # [N, 32, 128]
        f, _ = self.rnn(f)                 # [N, 32, 128]
        logits = self.saida(f)             # [N, 32, C]
        return logits.permute(1, 0, 2)     # [T, N, C]  (formato do CTC)


def decodificar(logits: torch.Tensor) -> list[tuple[str, float]]:
    """CTC guloso: devolve (texto, confiança) por amostra; confiança = média do máximo por passo não-branco."""
    probs = logits.softmax(-1)             # [T, N, C]
    conf, idx = probs.max(-1)              # [T, N]
    saidas = []
    for n in range(idx.shape[1]):
        texto, ultimo, confs = [], 0, []
        for t in range(idx.shape[0]):
            k = int(idx[t, n])
            if k != 0 and k != ultimo:
                texto.append(ALFABETO[k - 1])
                confs.append(float(conf[t, n]))
            ultimo = k
        saidas.append(("".join(texto), sum(confs) / len(confs) if confs else 0.0))
    return saidas
