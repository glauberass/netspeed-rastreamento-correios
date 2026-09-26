using NetSpeed.Domain.Excecoes;
using NetSpeed.Domain.Modelos;

namespace NetSpeed.Tests;

public class CodigoObjetoTests
{
    [Theory]
    [InlineData("NN437873753BR", "NN437873753BR")]
    [InlineData("nn437873753br", "NN437873753BR")]
    [InlineData(" NN 437 873 753 BR ", "NN437873753BR")]
    [InlineData("NN437873753-BR", "NN437873753BR")]
    public void Normaliza_maiusculas_e_remove_separadores(string entrada, string esperado) =>
        Assert.Equal(esperado, CodigoObjeto.Normalizar(entrada));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NN43787375BR")]     // 8 dígitos
    [InlineData("NN4378737530BR")]   // 10 dígitos
    [InlineData("1N437873753BR")]    // começa com dígito
    [InlineData("NN437873753")]      // sem sufixo do país
    [InlineData("12345678901")]      // CPF: fora do escopo deste robô
    [InlineData("XX000")]
    public void Recusa_codigo_com_formato_invalido(string? entrada)
    {
        var ex = Assert.Throws<RastreamentoException>(() => CodigoObjeto.Normalizar(entrada));

        Assert.Equal(CodigoErro.CodigoInvalido, ex.Codigo);
        Assert.False(CodigoObjeto.EhValido(entrada));
    }
}
