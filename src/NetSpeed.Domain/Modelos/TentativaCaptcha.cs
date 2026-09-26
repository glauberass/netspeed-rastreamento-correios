namespace NetSpeed.Domain.Modelos;

/// <summary>Registro de uma tentativa de resolver o CAPTCHA (o enunciado exige registrá-las).</summary>
/// <param name="Numero">Número da tentativa dentro da consulta (1 = primeira).</param>
/// <param name="Texto">Texto que o solver reconheceu (vazio se nada foi reconhecido).</param>
/// <param name="Aceito">Se o portal aceitou o texto.</param>
/// <param name="DuracaoMs">Tempo gasto na tentativa (obter imagem + OCR + validação).</param>
/// <param name="Observacao">Detalhe opcional (ex.: "solver não reconheceu nada").</param>
public sealed record TentativaCaptcha(
    int Numero,
    string Texto,
    bool Aceito,
    long DuracaoMs,
    string? Observacao = null);
