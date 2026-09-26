using Microsoft.Extensions.Configuration;
using NetSpeed.Domain.Configuracao;
using Serilog;
using Serilog.Events;

namespace NetSpeed.DependencyInjection;

/// <summary>Configuração do Serilog compartilhada pelos dois hosts.</summary>
public static class LogConfiguracao
{
    /// <summary>
    /// Console com o essencial; arquivo texto e arquivo JSON estruturado com todo o detalhe.
    /// Os arquivos ficam em <c>logs/</c> (rotação diária).
    /// </summary>
    public static Serilog.Core.Logger Criar(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var log = new LogSettings();
        configuration.GetSection($"{nameof(AppSettings)}:{nameof(AppSettings.Log)}").Bind(log);

        var diretorio = Path.IsPathRooted(log.Diretorio)
            ? log.Diretorio
            : Path.Combine(AppContext.BaseDirectory, log.Diretorio);
        Directory.CreateDirectory(diretorio);

        return new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console(
                restrictedToMinimumLevel: LogEventLevel.Information,
                standardErrorFromLevel: LogEventLevel.Verbose, // stdout fica livre para o JSON
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                path: Path.Combine(diretorio, "netspeed-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: log.ArquivosRetidos,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                formatter: new Serilog.Formatting.Json.JsonFormatter(renderMessage: true),
                path: Path.Combine(diretorio, "netspeed-estruturado-.json"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: log.ArquivosRetidos)
            .CreateLogger();
    }
}
