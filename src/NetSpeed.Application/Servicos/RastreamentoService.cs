using System.Diagnostics;
using System.Dynamic;
using Microsoft.Extensions.Logging;
using NetSpeed.Captcha;
using NetSpeed.Domain.Configuracao;
using NetSpeed.Domain.Excecoes;
using NetSpeed.Domain.Modelos;
using NetSpeed.Domain.Portas;
using NetSpeed.Pipelines;
using PipeliningLibrary;

namespace NetSpeed.Application.Servicos;

/// <summary>
/// Consulta objetos: valida o código, monta o contexto, roda o pipeline e aplica retry.
/// </summary>
/// <remarks>
/// Os dois grupos de pipeline são criados uma vez (a composição é imutável). O de encerramento
/// roda no <c>finally</c> de cada tentativa — o navegador fecha mesmo quando a consulta falha.
/// Cada tentativa usa um navegador novo: cookies e CAPTCHA de uma sessão que falhou não
/// contaminam a seguinte.
/// </remarks>
public sealed class RastreamentoService(
    AppSettings settings,
    IPaginaRastreamentoFactory fabrica,
    ResolvedorDeCaptchaNoPortal resolvedor,
    PoliticaDeRetry retry,
    ILogger<RastreamentoService> logger) : IConsultaRastreamento
{
    private readonly PipelineController _controller = new();
    private readonly PipelineComum _comum = new();

    public async Task<RastreamentoResultado> ConsultarAsync(string codigo, CancellationToken ct = default)
    {
        string normalizado;
        try
        {
            normalizado = CodigoObjeto.Normalizar(codigo);
        }
        catch (RastreamentoException ex)
        {
            // Código inválido: resposta imediata, sem abrir navegador nem gastar limite do portal.
            logger.LogWarning("Código rejeitado antes da consulta: {Mensagem}", ex.Message);
            return new RastreamentoResultado
            {
                CodigoObjeto = codigo?.Trim() ?? string.Empty,
                Sucesso = false,
                Erro = ex.ParaErro()
            };
        }

        var resultado = new RastreamentoResultado { CodigoObjeto = normalizado };
        var inicio = Stopwatch.GetTimestamp();

        try
        {
            await retry.ExecutarAsync(
                    tentativa => Task.Run(() => ExecutarPipeline(resultado, tentativa, ct), ct),
                    ex => ex is RastreamentoException { Codigo: CodigoErro.PortalIndisponivel or CodigoErro.Timeout },
                    ct)
                .ConfigureAwait(false);
        }
        catch (RastreamentoException ex)
        {
            // Retries esgotados numa falha transitória.
            resultado.Sucesso = false;
            resultado.Erro = ex.ParaErro();
            logger.LogError("[{Codigo}] desistindo após retries: {Erro} — {Mensagem}", normalizado, ex.Codigo, ex.Message);
        }

        // Duração total da consulta, incluindo as tentativas repetidas (retry).
        resultado.DuracaoMs = (long)Stopwatch.GetElapsedTime(inicio).TotalMilliseconds;

        return resultado;
    }

    public async Task<IReadOnlyList<RastreamentoResultado>> ConsultarVariosAsync(
        IEnumerable<string> codigos,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(codigos);

        var lista = codigos
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => CodigoObjeto.EhValido(c) ? CodigoObjeto.Normalizar(c) : c.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var resultados = new RastreamentoResultado?[lista.Count];
        using var limite = new SemaphoreSlim(Math.Max(1, settings.Lote.MaxParalelismo));

        var tarefas = lista.Select(async (codigo, indice) =>
        {
            await limite.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (indice > 0 && settings.Lote.IntervaloEntreConsultasMs > 0)
                {
                    await Task.Delay(settings.Lote.IntervaloEntreConsultasMs, ct).ConfigureAwait(false);
                }

                resultados[indice] = await ConsultarAsync(codigo, ct).ConfigureAwait(false);
            }
            finally
            {
                limite.Release();
            }
        });

        await Task.WhenAll(tarefas).ConfigureAwait(false);
        return resultados!;
    }

    private RastreamentoResultado ExecutarPipeline(RastreamentoResultado resultado, int tentativa, CancellationToken ct)
    {
        dynamic contexto = new ExpandoObject();
        contexto.Resultado = resultado;
        contexto.Codigo = resultado.CodigoObjeto;
        contexto.Settings = settings;
        contexto.PaginaFactory = fabrica;
        contexto.Resolvedor = resolvedor;
        contexto.Logger = logger;
        contexto.CancellationToken = ct;

        logger.LogInformation("[{Codigo}] tentativa de consulta {Tentativa}", resultado.CodigoObjeto, tentativa);

        try
        {
            PipelineResult retorno = _controller["ConsultarObjeto"].RunDetailed(contexto);

            // O Pipelining captura exceções dos pipes; as transitórias precisam voltar como
            // exceção para a política de retry enxergá-las.
            if (retorno.Exception() is { } falha)
            {
                throw Desembrulhar(falha);
            }

            return resultado;
        }
        finally
        {
            _comum["FinalizarConsulta"].Run(contexto);
        }
    }

    private static Exception Desembrulhar(Exception ex)
    {
        for (Exception? atual = ex; atual is not null; atual = atual.InnerException)
        {
            if (atual is RastreamentoException)
            {
                return atual;
            }
        }

        return ex;
    }
}
