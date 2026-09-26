using NetSpeed.Domain.Modelos;
using NetSpeed.Web.Jobs;

namespace NetSpeed.Web.Endpoints;

/// <summary>Corpo do POST: um ou mais códigos de rastreamento.</summary>
public sealed record SolicitacaoDeConsulta(IReadOnlyList<string>? Codigos);

public static class RastreamentoEndpoints
{
    /// <summary>Máximo de códigos por solicitação (cada um abre um navegador e gasta limite do portal).</summary>
    private const int MaximoPorSolicitacao = 20;

    public static void MapRastreamentos(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/api/rastreamentos");

        // Enfileira e devolve 202 + Location; o resultado sai em GET /{id} quando o job terminar.
        grupo.MapPost("/", (SolicitacaoDeConsulta solicitacao, FilaDeJobs fila) =>
        {
            var codigos = (solicitacao.Codigos ?? [])
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim())
                .ToList();

            if (codigos.Count == 0)
            {
                return Results.BadRequest(new { erro = "Informe ao menos um código de rastreamento em 'codigos'." });
            }

            if (codigos.Count > MaximoPorSolicitacao)
            {
                return Results.BadRequest(new { erro = $"No máximo {MaximoPorSolicitacao} códigos por solicitação." });
            }

            var job = fila.Enfileirar(codigos);
            return Results.Accepted($"/api/rastreamentos/{job.Id}", Resumo(job));
        });

        grupo.MapGet("/{id:guid}", (Guid id, FilaDeJobs fila) =>
            fila.Obter(id) is { } job ? Results.Ok(Detalhe(job)) : Results.NotFound(new { erro = "Job não encontrado." }));

        grupo.MapGet("/", (FilaDeJobs fila) => Results.Ok(fila.Recentes().Select(Resumo)));

        rotas.MapGet("/api/saude", () => Results.Ok(new { status = "ok", hora = DateTimeOffset.Now }));
    }

    private static object Resumo(JobDeConsulta job) => new
    {
        job.Id,
        job.Status,
        job.Codigos,
        job.CriadoEm,
        job.FinalizadoEm
    };

    private static object Detalhe(JobDeConsulta job) => new
    {
        job.Id,
        job.Status,
        job.Codigos,
        job.CriadoEm,
        job.FinalizadoEm,
        job.MensagemDeFalha,
        // Uma consulta devolve o objeto; várias, o array — mesmo formato do Executor.
        Resultados = (IReadOnlyList<RastreamentoResultado>)job.Resultados
    };
}
