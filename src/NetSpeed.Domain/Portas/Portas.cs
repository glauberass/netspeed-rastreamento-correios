using NetSpeed.Domain.Modelos;

namespace NetSpeed.Domain.Portas;

/// <summary>O que o solver leu de uma imagem de CAPTCHA.</summary>
/// <param name="Texto">Texto reconhecido; vazio se nada plausível foi lido.</param>
/// <param name="Confianca">Confiança da leitura, de 0 a 1 (0 quando não há leitura).</param>
public readonly record struct LeituraCaptcha(string Texto, double Confianca)
{
    public static LeituraCaptcha Vazia => new(string.Empty, 0);

    public bool TemTexto => Texto.Length > 0;
}

/// <summary>Resolve um CAPTCHA de imagem. Não sabe nada de navegador nem de portal.</summary>
public interface ICaptchaSolver
{
    LeituraCaptcha Resolver(byte[] imagem);
}

/// <summary>Consulta um código no portal. Toda a navegação fica atrás desta porta.</summary>
public interface IConsultaRastreamento
{
    /// <summary>
    /// Consulta um objeto. Nunca lança por falha de negócio: o erro vem em <see cref="RastreamentoResultado.Erro"/>.
    /// </summary>
    Task<RastreamentoResultado> ConsultarAsync(string codigo, CancellationToken ct = default);

    /// <summary>Consulta vários objetos (sem repetir códigos), na mesma ordem em que foram informados.</summary>
    Task<IReadOnlyList<RastreamentoResultado>> ConsultarVariosAsync(IEnumerable<string> codigos, CancellationToken ct = default);
}

/// <summary>Grava o resultado em arquivo .json.</summary>
public interface IExportadorJson
{
    /// <summary>Grava e devolve o caminho do arquivo. Sem caminho, usa a pasta configurada.</summary>
    Task<string> ExportarAsync(IReadOnlyCollection<RastreamentoResultado> resultados, string? caminho, CancellationToken ct = default);

    string Serializar(object valor);
}
