using Microsoft.Extensions.Logging;
using NetSpeed.Domain.Portas;

namespace NetSpeed.Captcha;

/// <summary>
/// Combina dois solvers: usa o principal e só recorre ao secundário quando o principal não leu nada.
/// </summary>
/// <remarks>
/// Não mistura as duas leituras (não há como saber qual acertou antes de submeter ao portal);
/// só evita desperdiçar uma imagem quando o solver principal desiste dela.
/// </remarks>
public sealed class SolverComFallback(
    ICaptchaSolver principal,
    ICaptchaSolver secundario,
    ILogger<SolverComFallback>? logger = null) : ICaptchaSolver, IDisposable
{
    public LeituraCaptcha Resolver(byte[] imagem)
    {
        var leitura = principal.Resolver(imagem);
        if (leitura.TemTexto)
        {
            return leitura;
        }

        logger?.LogDebug("Solver principal sem leitura; tentando o secundário");
        return secundario.Resolver(imagem);
    }

    public void Dispose()
    {
        (principal as IDisposable)?.Dispose();
        (secundario as IDisposable)?.Dispose();
    }
}
