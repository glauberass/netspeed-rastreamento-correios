using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NetSpeed.Domain.Portas;
using Tesseract;

namespace NetSpeed.Captcha;

/// <summary>
/// Solver de CAPTCHA por OCR local: pré-processamento por cor + Tesseract com votação entre
/// variantes da mesma imagem.
/// </summary>
/// <remarks>
/// <para>
/// O reconhecimento roda sobre <see cref="CaptchaImagePreprocessor.GerarVariantes"/>. Cada
/// variante gera uma leitura com confiança; o texto vencedor é o que mais se repete (desempate
/// pela maior confiança). Só entram leituras plausíveis: apenas <c>a-z0-9</c> e de 4 a 6
/// caracteres, que é o que o Securimage do portal emite.
/// </para>
/// <para>
/// <see cref="TesseractEngine"/> não é thread-safe: o acesso é serializado por lock. Para
/// paralelismo, use uma instância por thread — o solver é barato de criar (carrega ~4 MB).
/// </para>
/// </remarks>
public sealed partial class TesseractCaptchaSolver : ICaptchaSolver, IDisposable
{
    private const int TamanhoMinimo = 4;
    private const int TamanhoMaximo = 6;

    private readonly TesseractEngine _engine;
    private readonly ILogger<TesseractCaptchaSolver>? _logger;
    private readonly object _trava = new();

    public TesseractCaptchaSolver(string diretorioTessdata, ILogger<TesseractCaptchaSolver>? logger = null)
    {
        _logger = logger;
        _engine = new TesseractEngine(diretorioTessdata, "eng", EngineMode.LstmOnly);
        _engine.SetVariable("tessedit_char_whitelist", "abcdefghijklmnopqrstuvwxyz0123456789");
        _engine.DefaultPageSegMode = PageSegMode.SingleLine;
    }

    public LeituraCaptcha Resolver(byte[] imagem)
    {
        var variantes = CaptchaImagePreprocessor.GerarVariantes(imagem);
        if (variantes.Count == 0)
        {
            _logger?.LogDebug("Imagem sem letras após a limpeza; nada a reconhecer");
            return LeituraCaptcha.Vazia;
        }

        var leituras = new List<(string Texto, float Confianca)>();

        lock (_trava)
        {
            foreach (var variante in variantes)
            {
                var leitura = Ler(variante);
                if (leitura is not null)
                {
                    leituras.Add(leitura.Value);
                }
            }
        }

        if (leituras.Count == 0)
        {
            return LeituraCaptcha.Vazia;
        }

        var vencedor = leituras
            .GroupBy(l => l.Texto)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Max(l => l.Confianca))
            .First();

        _logger?.LogDebug(
            "OCR: {Leituras} => {Texto}",
            string.Join(", ", leituras.Select(l => $"{l.Texto}({l.Confianca:P0})")),
            vencedor.Key);

        // Confiança = confiança média do vencedor x fração das variantes que concordaram.
        var concordancia = vencedor.Count() / (double)variantes.Count;
        return new LeituraCaptcha(vencedor.Key, vencedor.Average(l => l.Confianca) / 100.0 * concordancia);
    }

    public void Dispose() => _engine.Dispose();

    private (string Texto, float Confianca)? Ler(byte[] png)
    {
        using var pix = Pix.LoadFromMemory(png);
        using var pagina = _engine.Process(pix);

        var texto = NaoAlfanumerico().Replace(pagina.GetText() ?? string.Empty, string.Empty).ToLowerInvariant();

        if (texto.Length is < TamanhoMinimo or > TamanhoMaximo)
        {
            return null;
        }

        return (texto, pagina.GetMeanConfidence());
    }

    [GeneratedRegex("[^a-z0-9]", RegexOptions.IgnoreCase)]
    private static partial Regex NaoAlfanumerico();
}
