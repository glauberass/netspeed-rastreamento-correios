using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetSpeed.Application.Servicos;
using NetSpeed.Captcha;
using NetSpeed.Domain.Configuracao;
using NetSpeed.Domain.Portas;
using NetSpeed.Infrastructure.Portal;
using NetSpeed.Infrastructure.Saida;
using Serilog;

namespace NetSpeed.DependencyInjection;

/// <summary>Composição do grafo de dependências.</summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNetSpeed(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddSerilog(dispose: false);
        });

        var settings = new AppSettings();
        configuration.GetSection(nameof(AppSettings)).Bind(settings);
        services.AddSingleton(settings);
        services.AddSingleton(settings.Captcha);
        services.AddSingleton(settings.Retry);

        // Solver: singleton (os modelos são carregados uma vez). A porta permite trocar a
        // estratégia sem mexer em mais nenhuma linha. Principal: rede neural (ONNX). Secundário
        // (quando a rede não lê nada, ou se o modelo não estiver presente): Tesseract.
        services.AddSingleton<ICaptchaSolver>(sp =>
        {
            static string Resolver(string caminho) =>
                Path.IsPathRooted(caminho) ? caminho : Path.Combine(AppContext.BaseDirectory, caminho);

            var captcha = settings.Captcha;
            var log = sp.GetRequiredService<ILoggerFactory>();
            var logger = log.CreateLogger("NetSpeed.DependencyInjection");

            // O Tesseract usa binários nativos de Windows x64: em outro ambiente pode não carregar.
            // Nesse caso segue só com a rede neural (que é multiplataforma), em vez de falhar.
            TesseractCaptchaSolver? tesseract = null;
            try
            {
                tesseract = new TesseractCaptchaSolver(
                    Resolver(captcha.DiretorioTessdata), log.CreateLogger<TesseractCaptchaSolver>());
            }
#pragma warning disable CA1031 // Qualquer falha ao carregar o fallback é tolerável.
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Tesseract indisponível; seguindo sem o solver secundário");
            }
#pragma warning restore CA1031

            var modelo = Resolver(captcha.ArquivoModelo);
            if (!File.Exists(modelo))
            {
                logger.LogWarning(
                    "Modelo neural não encontrado em {Modelo}; usando apenas o Tesseract (acerto bem menor)", modelo);
                return tesseract ?? throw new FileNotFoundException(
                    "Nenhum solver de CAPTCHA disponível: modelo neural ausente e Tesseract indisponível.", modelo);
            }

            var neural = new RedeNeuralCaptchaSolver(modelo, log.CreateLogger<RedeNeuralCaptchaSolver>());
            return tesseract is null
                ? neural
                : new SolverComFallback(neural, tesseract, log.CreateLogger<SolverComFallback>());
        });

        services.AddSingleton<ResolvedorDeCaptchaNoPortal>();
        services.AddSingleton<PoliticaDeRetry>();
        services.AddSingleton<IPaginaRastreamentoFactory, PaginaCorreiosFactory>();
        services.AddSingleton<IExportadorJson, ExportadorJson>();
        services.AddSingleton<IConsultaRastreamento, RastreamentoService>();

        return services;
    }
}
