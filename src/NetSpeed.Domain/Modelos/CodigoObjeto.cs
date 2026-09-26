using System.Text.RegularExpressions;
using NetSpeed.Domain.Excecoes;

namespace NetSpeed.Domain.Modelos;

/// <summary>Normalização e validação do código de rastreamento (2 letras, 9 dígitos, 2 letras).</summary>
public static partial class CodigoObjeto
{
    [GeneratedRegex("^[A-Z]{2}[0-9]{9}[A-Z]{2}$")]
    private static partial Regex Formato();

    /// <summary>Remove espaços/pontuação, passa para maiúsculas e valida o formato.</summary>
    public static string Normalizar(string? codigo)
    {
        var limpo = Limpar(codigo);

        if (!Formato().IsMatch(limpo))
        {
            throw new RastreamentoException(
                CodigoErro.CodigoInvalido,
                $"Código de rastreamento inválido: '{codigo}'. Formato esperado: AA123456789BR.");
        }

        return limpo;
    }

    public static bool EhValido(string? codigo) => Formato().IsMatch(Limpar(codigo));

    private static string Limpar(string? codigo) =>
        new string((codigo ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
}
