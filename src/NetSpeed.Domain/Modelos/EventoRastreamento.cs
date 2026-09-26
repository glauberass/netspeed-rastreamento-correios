namespace NetSpeed.Domain.Modelos;

/// <summary>
/// Um evento do histórico de rastreamento.
/// </summary>
/// <remarks>
/// Nem todo evento tem todos os campos: um "Objeto entregue" traz só <c>Local</c>, enquanto um
/// "Objeto em transferência" traz <c>Origem</c> e <c>Destino</c>. Campo que o portal não
/// apresentou fica <c>null</c> — nunca string vazia — para o consumidor distinguir "não veio" de
/// "veio vazio".
/// </remarks>
public sealed record EventoRastreamento
{
    /// <summary>Título do evento (ex.: "Objeto entregue ao destinatário").</summary>
    public required string Status { get; init; }

    /// <summary>Texto complementar do evento, quando existir.</summary>
    public string? Descricao { get; init; }

    /// <summary>Data e hora do evento (horário de Brasília, sem fuso), quando o portal informou.</summary>
    public DateTime? DataHora { get; init; }

    /// <summary>Texto original de data/hora, preservado para auditoria.</summary>
    public string? DataHoraOriginal { get; init; }

    /// <summary>Local onde o evento ocorreu (eventos que não são movimentação entre unidades).</summary>
    public string? Local { get; init; }

    /// <summary>Unidade de origem (eventos de transferência/encaminhamento).</summary>
    public string? Origem { get; init; }

    /// <summary>Unidade de destino (eventos de transferência/encaminhamento).</summary>
    public string? Destino { get; init; }

    /// <summary>Qualquer outra informação do evento que não se encaixe nos campos acima.</summary>
    public IReadOnlyList<string> InformacoesAdicionais { get; init; } = [];
}
