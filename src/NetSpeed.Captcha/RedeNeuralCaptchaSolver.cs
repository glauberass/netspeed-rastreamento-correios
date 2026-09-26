using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using NetSpeed.Domain.Portas;

namespace NetSpeed.Captcha;

/// <summary>
/// Solver principal: rede convolucional-recorrente (CRNN + CTC) treinada para este CAPTCHA,
/// executada em C# via ONNX Runtime.
/// </summary>
/// <remarks>
/// <para>
/// <b>Fluxo.</b> Imagem do portal → <see cref="CaptchaImagePreprocessor.GerarMascara"/> (remove
/// linhas e pontos por cor) → <see cref="CaptchaImagePreprocessor.ParaTensor"/> (recorte +
/// 128x32) → rede → logits <c>[32, 1, 37]</c> → decodificação CTC gulosa. O pré-processamento é o
/// mesmo do treino (o C# é a fonte única), então não há divergência entre o que a rede viu ao
/// aprender e o que vê em produção.
/// </para>
/// <para>
/// <b>Confiança.</b> Média da probabilidade máxima nos passos que emitiram caractere. Serve para
/// descartar leituras duvidosas sem gastar uma consulta ao portal (ver
/// <c>CaptchaSettings.ConfiancaMinima</c>).
/// </para>
/// <para>
/// <b>Como o modelo foi obtido:</b> ver <c>tools/treinamento/README.md</c> (dados sintéticos +
/// amostras reais rotuladas, treino em PyTorch, exportação ONNX).
/// </para>
/// </remarks>
public sealed class RedeNeuralCaptchaSolver : ICaptchaSolver, IDisposable
{
    private const string Alfabeto = "abcdefghijklmnopqrstuvwxyz0123456789";
    /// <summary>O CAPTCHA do portal tem de 4 a 6 caracteres (conferido em dezenas de imagens reais).</summary>
    private const int TamanhoMinimo = 4;
    private const int TamanhoMaximo = 6;

    private readonly InferenceSession _sessao;
    private readonly string _entrada;
    private readonly ILogger<RedeNeuralCaptchaSolver>? _logger;

    public RedeNeuralCaptchaSolver(string caminhoModelo, ILogger<RedeNeuralCaptchaSolver>? logger = null)
    {
        if (!File.Exists(caminhoModelo))
        {
            throw new FileNotFoundException($"Modelo da rede neural não encontrado: {caminhoModelo}", caminhoModelo);
        }

        _logger = logger;
        _sessao = new InferenceSession(caminhoModelo);
        _entrada = _sessao.InputMetadata.Keys.First();
    }

    public LeituraCaptcha Resolver(byte[] imagem)
    {
        var mascara = CaptchaImagePreprocessor.GerarMascara(imagem);
        if (mascara is null)
        {
            _logger?.LogDebug("Imagem sem letras após a limpeza; nada a reconhecer");
            return LeituraCaptcha.Vazia;
        }

        var dados = CaptchaImagePreprocessor.ParaTensor(mascara);
        var entrada = new DenseTensor<float>(
            dados,
            [1, 1, CaptchaImagePreprocessor.TensorAltura, CaptchaImagePreprocessor.TensorLargura]);

        using var saida = _sessao.Run([NamedOnnxValue.CreateFromTensor(_entrada, entrada)]);
        var logits = saida.First().AsTensor<float>(); // [T, 1, C]

        var leitura = DecodificarCtc(logits);

        _logger?.LogDebug("Rede neural: '{Texto}' (confiança {Confianca:P0})", leitura.Texto, leitura.Confianca);
        return leitura;
    }

    public void Dispose() => _sessao.Dispose();

    /// <summary>Decodificação CTC gulosa: argmax por passo, colapsa repetições e descarta o branco (índice 0).</summary>
    internal static LeituraCaptcha DecodificarCtc(Tensor<float> logits)
    {
        var passos = logits.Dimensions[0];
        var classes = logits.Dimensions[2];

        var texto = new System.Text.StringBuilder();
        var confiancas = new List<double>();
        var anterior = 0;

        for (var t = 0; t < passos; t++)
        {
            // softmax estável só para o melhor índice
            var max = float.NegativeInfinity;
            var melhor = 0;
            for (var c = 0; c < classes; c++)
            {
                var v = logits[t, 0, c];
                if (v > max)
                {
                    max = v;
                    melhor = c;
                }
            }

            double soma = 0;
            for (var c = 0; c < classes; c++)
            {
                soma += Math.Exp(logits[t, 0, c] - max);
            }

            if (melhor != 0 && melhor != anterior && melhor <= Alfabeto.Length)
            {
                texto.Append(Alfabeto[melhor - 1]);
                confiancas.Add(1.0 / soma);
            }

            anterior = melhor;
        }

        if (texto.Length is < TamanhoMinimo or > TamanhoMaximo)
        {
            return LeituraCaptcha.Vazia;
        }

        return new LeituraCaptcha(texto.ToString(), confiancas.Average());
    }
}
