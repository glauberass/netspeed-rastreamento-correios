using Microsoft.Extensions.Logging;
using NetSpeed.Domain.Configuracao;

namespace NetSpeed.Application.Servicos;

/// <summary>
/// Retentativa com backoff exponencial para falhas transitórias (portal fora, timeout).
/// </summary>
/// <remarks>
/// Só repete o que vale a pena repetir: quem decide é o predicado <c>transitoria</c>. CAPTCHA
/// não resolvido, objeto inexistente e código inválido são respostas definitivas — repetir só
/// gastaria requisições do limite do portal. A espera é uma função injetável para os testes não
/// dormirem de verdade.
/// </remarks>
public sealed class PoliticaDeRetry(RetrySettings settings, ILogger<PoliticaDeRetry> logger)
{
    public Func<TimeSpan, CancellationToken, Task> Esperar { get; init; } = Task.Delay;

    public async Task<T> ExecutarAsync<T>(
        Func<int, Task<T>> operacao,
        Func<Exception, bool> transitoria,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(operacao);
        ArgumentNullException.ThrowIfNull(transitoria);

        var max = Math.Max(1, settings.MaxTentativasConsulta);
        var espera = TimeSpan.FromMilliseconds(settings.EsperaInicialMs);

        for (var tentativa = 1; ; tentativa++)
        {
            try
            {
                return await operacao(tentativa).ConfigureAwait(false);
            }
            catch (Exception ex) when (tentativa < max && transitoria(ex) && !ct.IsCancellationRequested)
            {
                logger.LogWarning(
                    "Tentativa {Tentativa}/{Max} falhou ({Erro}); nova tentativa em {Espera} ms",
                    tentativa, max, ex.Message, (int)espera.TotalMilliseconds);

                await Esperar(espera, ct).ConfigureAwait(false);
                espera = TimeSpan.FromMilliseconds(espera.TotalMilliseconds * settings.FatorBackoff);
            }
        }
    }
}
