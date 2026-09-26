using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NetSpeed.Domain.Excecoes;
using NetSpeed.Domain.Modelos;
using PipeliningLibrary;

namespace NetSpeed.Pipelines.Pipes;

/// <summary>
/// Ponto de entrada do fluxo de consulta: compõe os sub-pipes e converte falhas em resultado.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que a falha não sobe como exceção.</b> Erros de negócio (objeto inexistente, CAPTCHA
/// não resolvido) e de infraestrutura (portal fora, timeout) viram
/// <see cref="RastreamentoResultado.Erro"/> com código estável. O chamador sempre recebe um
/// resultado — em lote, um código com problema não derruba os outros.
/// </para>
/// <para>
/// Falhas <em>transitórias</em> (<see cref="CodigoErro.PortalIndisponivel"/> e
/// <see cref="CodigoErro.Timeout"/>) são relançadas para o serviço tentar de novo com backoff;
/// o serviço só as converte em resultado quando as tentativas acabam.
/// </para>
/// </remarks>
public sealed class ConsultarObjetoStartPipe : IPipe
{
    public object Run(dynamic input)
    {
        RastreamentoResultado resultado = input.Resultado;
        ILogger logger = input.Logger;
        var inicio = Stopwatch.GetTimestamp();

        logger.LogInformation("[{Codigo}] -------- INÍCIO DA CONSULTA --------", resultado.CodigoObjeto);

        try
        {
            input = new AbrirPaginaPipe().Run(input);
            input = new QuebrarCaptchaPipe().Run(input);
            input = new ExtrairEventosPipe().Run(input);

            resultado.Sucesso = true;
            logger.LogInformation(
                "[{Codigo}] {Total} evento(s) extraído(s)",
                resultado.CodigoObjeto,
                resultado.Eventos.Count);
        }
        catch (RastreamentoException ex) when (ex.Codigo is CodigoErro.PortalIndisponivel or CodigoErro.Timeout)
        {
            // Transitório: quem decide desistir é a política de retry do serviço.
            logger.LogWarning("[{Codigo}] falha transitória ({Erro}): {Mensagem}", resultado.CodigoObjeto, ex.Codigo, ex.Message);
            throw;
        }
        catch (RastreamentoException ex)
        {
            resultado.Sucesso = false;
            resultado.Erro = ex.ParaErro();
            logger.LogError("[{Codigo}] {Erro}: {Mensagem}", resultado.CodigoObjeto, ex.Codigo, ex.Message);
        }
#pragma warning disable CA1031 // A fronteira do start pipe é onde o inesperado vira resultado.
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            resultado.Sucesso = false;
            resultado.Erro = new ErroRastreamento(CodigoErro.ErroInesperado, $"{ex.GetType().Name}: {ex.Message}");
            logger.LogError(ex, "[{Codigo}] erro inesperado", resultado.CodigoObjeto);
        }
#pragma warning restore CA1031
        finally
        {
            resultado.DuracaoMs = (long)Stopwatch.GetElapsedTime(inicio).TotalMilliseconds;
            logger.LogInformation("[{Codigo}] -------- FIM DA CONSULTA ({Ms} ms) --------", resultado.CodigoObjeto, resultado.DuracaoMs);
        }

        return input;
    }
}
