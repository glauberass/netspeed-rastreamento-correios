# Treinamento do reconhecedor de CAPTCHA

Esta pasta contém **tudo que foi usado para produzir** `modelos/captcha-crnn.onnx`, o modelo que o
solver principal (`RedeNeuralCaptchaSolver`) executa em C#. Nada aqui é necessário para *rodar* a
solução — só para reproduzir ou melhorar o modelo.

## Ideia em uma frase

O CAPTCHA do portal (Securimage) desenha cada camada com um tom de cinza próprio; o C# separa as
letras por cor, e uma rede pequena (CRNN + CTC) lê a máscara resultante.

```
imagem 215x80 ──► [C#] máscara das letras ──► recorte 128x32 ──► [CRNN/ONNX] ──► texto + confiança
                  (remove linhas e pontos)     (ParaTensor)
```

## Arquivos

| Arquivo | Função |
|---|---|
| `sintetico.py` | Gera CAPTCHAs sintéticos **já no domínio da máscara** (letras deformadas + defeitos que a limpeza deixa). Usa fontes bold do Windows parecidas com a do Securimage. |
| `modelo.py` | A rede: 5 blocos convolucionais → BiGRU (2 camadas) → CTC. ~0,4 M parâmetros. |
| `treinar.py` | Fase 1: só sintético. Fase 2: ajuste fino com amostras **reais** rotuladas (parte fica fora do treino para validação honesta). |
| `prever.py` | Lê tensores reais com o modelo; imprime leitura/confiança e compara com rótulos. |
| `exportar_onnx.py` | Exporta para ONNX e confere que ONNX Runtime == PyTorch. |
| `dados/reais.csv` | Rótulos das amostras reais (`arquivo,texto`). |
| `dados/brutas/` | Imagens originais do portal que têm rótulo (PNG 215x80). |

## Reprodução

```bash
python -m venv .venv && .venv/Scripts/activate
pip install torch --index-url https://download.pytorch.org/whl/cpu
pip install -r requirements.txt

# 1) tensores das imagens reais — gerados pelo MESMO C# usado em produção
dotnet run --project ../NetSpeed.CaptchaCalibrador -- tensor dados/brutas dados/tensores

# 2) fase 1: sintético
python treinar.py --sinteticos 40000 --epocas 14 --saida modelo_v1.pt

# 3) fase 2: ajuste fino com reais
python treinar.py --sinteticos 30000 --epocas 14 --reais dados/reais.csv --inicio modelo_v1.pt --saida modelo_v2.pt

# 4) exportar
python exportar_onnx.py modelo_v2.pt ../../modelos/captcha-crnn.onnx
```

## Por que gerar os tensores reais pelo C#

Treino e inferência precisam do **mesmo** pré-processamento; uma reimplementação em Python
divergiria em detalhes (filtro de redimensionamento, arredondamento) e o modelo perderia acerto
sem que nada avisasse. Com o C# como fonte única, o que a rede viu ao aprender é exatamente o que
ela vê em produção.

## Como os rótulos reais foram obtidos

Duas fontes, em ordem de confiança: (1) imagens que o **próprio portal aceitou** (rótulo
confirmado); (2) imagens lidas pelo modelo e **revisadas visualmente**, corrigindo os erros.
Os números de acerto no README principal são medidos contra o portal real, não contra os rótulos.
