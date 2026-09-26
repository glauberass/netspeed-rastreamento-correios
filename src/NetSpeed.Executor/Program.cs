using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetSpeed.DependencyInjection;
using NetSpeed.Domain.Portas;
using Serilog;

namespace NetSpeed.Executor;

/// <summary>
/// Host de linha de comando: lê argumentos, monta o contêiner, consulta e emite o JSON.
/// </summary>
/// <remarks>
/// O JSON vai para o stdout e os logs para o stderr, então <c>... | jq</c> ou redirecionamento
/// para arquivo funcionam sem filtrar nada. Código de saída: 0 = todas as consultas com
/// sucesso; 1 = ao menos uma falhou; 2 = uso incorreto.
/// </remarks>
internal static class Program
{
    private const string Uso = """
        Uso: NetSpeed.Executor <codigo> [<codigo> ...] [opcoes]

          <codigo>             Código de rastreamento (ex.: NN437873753BR). Pode repetir.
          --codigo <codigo>    Forma explícita de informar um código. Pode repetir.
          --arquivo <caminho>  Arquivo texto com um código por linha (# comenta).
          --saida <caminho>    Grava o JSON neste arquivo (além de imprimir no stdout).
          --visivel            Mostra o navegador (padrão: headless).
          --max-tentativas <n> Tentativas máximas de CAPTCHA por consulta.
          --paralelismo <n>    Consultas simultâneas no lote (padrão 1).
          --debug-captcha <d>  Salva cada imagem de CAPTCHA tentada na pasta <d>.
          -h, --help           Mostra esta ajuda.

        Exemplo:
          NetSpeed.Executor NN437873753BR --saida saida/NN437873753BR.json
        """;

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Any(a => a is "-h" or "--help"))
        {
            Console.WriteLine(Uso);
            return 0;
        }

        Argumentos parametros;
        try
        {
            parametros = Argumentos.Ler(args);
        }
        catch (ArgumentException ex)
        {
            await Console.Error.WriteLineAsync($"Erro: {ex.Message}\n\n{Uso}").ConfigureAwait(false);
            return 2;
        }

        if (parametros.Codigos.Count == 0)
        {
            await Console.Error.WriteLineAsync($"Erro: informe ao menos um código de rastreamento.\n\n{Uso}").ConfigureAwait(false);
            return 2;
        }

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddInMemoryCollection(parametros.Sobrescritas)
            .Build();

        Log.Logger = LogConfiguracao.Criar(configuration);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            var services = new ServiceCollection().AddNetSpeed(configuration);
            await using var provider = services.BuildServiceProvider();

            var consulta = provider.GetRequiredService<IConsultaRastreamento>();
            var exportador = provider.GetRequiredService<IExportadorJson>();

            var resultados = await consulta.ConsultarVariosAsync(parametros.Codigos, cts.Token).ConfigureAwait(false);

            // O JSON é sempre impresso; o arquivo só se pedido (ou sempre, se houver mais de um código).
            object conteudo = resultados.Count == 1 ? resultados[0] : resultados;
            Console.WriteLine(exportador.Serializar(conteudo));

            if (parametros.Saida is not null)
            {
                var caminho = await exportador.ExportarAsync(resultados, parametros.Saida, cts.Token).ConfigureAwait(false);
                Log.Information("JSON gravado em {Caminho}", caminho);
            }

            return resultados.All(r => r.Sucesso) ? 0 : 1;
        }
        catch (OperationCanceledException)
        {
            Log.Warning("Execução cancelada pelo usuário");
            return 130;
        }
#pragma warning disable CA1031 // O topo do processo converte qualquer falha em código de saída.
        catch (Exception ex)
        {
            Log.Fatal(ex, "Falha crítica na execução");
            return 1;
        }
#pragma warning restore CA1031
        finally
        {
            await Log.CloseAndFlushAsync().ConfigureAwait(false);
        }
    }
}
