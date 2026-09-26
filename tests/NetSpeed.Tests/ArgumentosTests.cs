using NetSpeed.Executor;

namespace NetSpeed.Tests;

public class ArgumentosTests
{
    [Fact]
    public void Codigos_posicionais_e_explicitos_sao_acumulados_na_ordem()
    {
        var a = Argumentos.Ler(["NN437873753BR", "--codigo", "AA123456789BR", "BB123456789BR"]);

        Assert.Equal(["NN437873753BR", "AA123456789BR", "BB123456789BR"], a.Codigos);
    }

    [Fact]
    public void Opcoes_viram_sobrescritas_do_appsettings()
    {
        var a = Argumentos.Ler(
            ["NN437873753BR", "--visivel", "--max-tentativas", "25", "--paralelismo", "2", "--debug-captcha", "debug", "--saida", "x.json"]);

        Assert.Equal("false", a.Sobrescritas["AppSettings:Navegador:Headless"]);
        Assert.Equal("25", a.Sobrescritas["AppSettings:Captcha:MaxTentativas"]);
        Assert.Equal("2", a.Sobrescritas["AppSettings:Lote:MaxParalelismo"]);
        Assert.Equal("debug", a.Sobrescritas["AppSettings:Captcha:DiretorioDebug"]);
        Assert.Equal("x.json", a.Saida);
    }

    [Fact]
    public void Arquivo_ignora_linhas_vazias_e_comentarios()
    {
        var caminho = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(caminho, ["NN437873753BR # o de teste", "", "# só comentário", "AA123456789BR"]);

            var a = Argumentos.Ler(["--arquivo", caminho]);

            Assert.Equal(["NN437873753BR", "AA123456789BR"], a.Codigos);
        }
        finally
        {
            File.Delete(caminho);
        }
    }

    [Theory]
    [InlineData("--codigo")]                       // falta o valor
    [InlineData("--max-tentativas", "abc")]        // não é inteiro
    [InlineData("--max-tentativas", "0")]          // não é positivo
    [InlineData("--opcao-que-nao-existe")]
    [InlineData("--arquivo", "nao-existe.txt")]
    public void Argumentos_invalidos_lancam_ArgumentException(params string[] args) =>
        Assert.Throws<ArgumentException>(() => Argumentos.Ler(args));
}
