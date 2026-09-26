namespace NetSpeed.Domain.Modelos;

/// <summary>Resultado da consulta de um objeto: dados, eventos, tentativas do CAPTCHA e erro tipado.</summary>
public sealed class RastreamentoResultado
{
    public required string CodigoObjeto { get; init; }

    public DateTimeOffset ConsultadoEm { get; init; } = DateTimeOffset.Now;

    public bool Sucesso { get; set; }

    /// <summary>Preenchido apenas quando <see cref="Sucesso"/> é falso.</summary>
    public ErroRastreamento? Erro { get; set; }

    /// <summary>Categoria do objeto no portal (ex.: "SEDEX"), quando informada.</summary>
    public string? TipoPostal { get; set; }

    public string? PrevisaoEntrega { get; set; }

    /// <summary>Eventos na mesma ordem do portal (do mais recente ao mais antigo).</summary>
    public IReadOnlyList<EventoRastreamento> Eventos { get; set; } = [];

    public IList<TentativaCaptcha> TentativasCaptcha { get; } = [];

    public long DuracaoMs { get; set; }
}

/// <summary>Erro estável e legível por máquina; <see cref="Codigo"/> não muda entre versões.</summary>
public sealed record ErroRastreamento(CodigoErro Codigo, string Mensagem);
