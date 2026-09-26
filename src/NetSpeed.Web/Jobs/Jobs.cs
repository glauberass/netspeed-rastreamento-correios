using System.Collections.Concurrent;
using System.Threading.Channels;
using NetSpeed.Domain.Modelos;
using NetSpeed.Domain.Portas;

namespace NetSpeed.Web.Jobs;

public enum StatusDoJob
{
    Pendente,
    Executando,
    Concluido,
    Falhou
}

/// <summary>Uma solicitação de consulta (um ou mais códigos) e o que se sabe dela agora.</summary>
public sealed class JobDeConsulta
{
    public required Guid Id { get; init; }

    public required IReadOnlyList<string> Codigos { get; init; }

    public DateTimeOffset CriadoEm { get; init; } = DateTimeOffset.Now;

    public StatusDoJob Status { get; set; } = StatusDoJob.Pendente;

    public DateTimeOffset? FinalizadoEm { get; set; }

    public string? MensagemDeFalha { get; set; }

    public IReadOnlyList<RastreamentoResultado> Resultados { get; set; } = [];
}

/// <summary>
/// Fila e armazenamento (em memória) dos jobs.
/// </summary>
/// <remarks>
/// Execução assíncrona: o POST só enfileira e devolve o id; quem consulta é o
/// <see cref="ProcessadorDeJobs"/>, em segundo plano. Um único consumidor de propósito: cada
/// consulta abre um navegador e o portal limita requisições por IP (HTTP 429), então enfileirar
/// é mais seguro do que paralelizar. Em produção o repositório seria um banco; a interface da API
/// não mudaria.
/// </remarks>
public sealed class FilaDeJobs
{
    private readonly ConcurrentDictionary<Guid, JobDeConsulta> _jobs = new();
    private readonly Channel<Guid> _canal = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });

    public JobDeConsulta Enfileirar(IReadOnlyList<string> codigos)
    {
        var job = new JobDeConsulta { Id = Guid.NewGuid(), Codigos = codigos };
        _jobs[job.Id] = job;
        _canal.Writer.TryWrite(job.Id);
        return job;
    }

    public JobDeConsulta? Obter(Guid id) => _jobs.GetValueOrDefault(id);

    public IReadOnlyList<JobDeConsulta> Recentes(int quantos = 20) =>
        [.. _jobs.Values.OrderByDescending(j => j.CriadoEm).Take(quantos)];

    public IAsyncEnumerable<Guid> AguardarAsync(CancellationToken ct) => _canal.Reader.ReadAllAsync(ct);
}

/// <summary>Consome a fila e executa cada job usando a mesma <see cref="IConsultaRastreamento"/> do Executor.</summary>
public sealed class ProcessadorDeJobs(
    FilaDeJobs fila,
    IConsultaRastreamento consulta,
    ILogger<ProcessadorDeJobs> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var id in fila.AguardarAsync(stoppingToken))
        {
            var job = fila.Obter(id);
            if (job is null)
            {
                continue;
            }

            job.Status = StatusDoJob.Executando;
            logger.LogInformation("Job {Id} iniciado ({Total} código(s))", id, job.Codigos.Count);

            try
            {
                job.Resultados = await consulta.ConsultarVariosAsync(job.Codigos, stoppingToken);
                job.Status = StatusDoJob.Concluido;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // Um job com problema não pode derrubar o processador.
            catch (Exception ex)
            {
                logger.LogError(ex, "Job {Id} falhou", id);
                job.Status = StatusDoJob.Falhou;
                job.MensagemDeFalha = ex.Message;
            }
#pragma warning restore CA1031
            finally
            {
                job.FinalizadoEm = DateTimeOffset.Now;
            }
        }
    }
}
