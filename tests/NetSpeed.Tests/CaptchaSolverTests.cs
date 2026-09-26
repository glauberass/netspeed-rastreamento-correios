using Microsoft.ML.OnnxRuntime.Tensors;
using NetSpeed.Captcha;
using NetSpeed.Domain.Portas;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace NetSpeed.Tests;

public class CaptchaPreprocessadorTests
{
    /// <summary>
    /// Imagem no formato do portal: fundo branco; letras em cinza 140; linha grossa em 112; linha
    /// fina em 117; pontinhos em 140.
    /// </summary>
    private static byte[] ImagemSintetica()
    {
        using var img = new Image<Rgb24>(215, 80, new Rgb24(255, 255, 255));

        // "letra": bloco 14x22 em 140
        for (var y = 30; y < 52; y++)
        {
            for (var x = 60; x < 74; x++)
            {
                img[x, y] = new Rgb24(140, 140, 140);
            }
        }

        // linha grossa horizontal em 112 (passando longe da letra)
        for (var x = 0; x < 215; x++)
        {
            for (var y = 10; y < 13; y++)
            {
                img[x, y] = new Rgb24(112, 112, 112);
            }
        }

        // linha fina em 117
        for (var x = 0; x < 215; x++)
        {
            img[x, 70] = new Rgb24(117, 117, 117);
        }

        // pontos isolados em 140 (ruído)
        foreach (var (x, y) in new[] { (5, 5), (150, 20), (180, 60), (30, 40) })
        {
            img[x, y] = new Rgb24(140, 140, 140);
        }

        using var ms = new MemoryStream();
        img.SaveAsPng(ms);
        return ms.ToArray();
    }

    [Fact]
    public void Mascara_mantem_a_letra_e_remove_linhas_e_pontos()
    {
        var mascara = CaptchaImagePreprocessor.GerarMascara(ImagemSintetica());

        Assert.NotNull(mascara);
        var tinta = mascara.Cast<bool>().Count(p => p);

        Assert.Equal(14 * 22, tinta);      // só o bloco da letra
        Assert.True(mascara[40, 65]);      // dentro da letra
        Assert.False(mascara[11, 100]);    // linha grossa removida
        Assert.False(mascara[70, 100]);    // linha fina removida
        Assert.False(mascara[5, 5]);       // ponto removido
    }

    [Fact]
    public void Imagem_so_com_ruido_nao_tem_mascara()
    {
        using var img = new Image<Rgb24>(215, 80, new Rgb24(255, 255, 255));
        img[10, 10] = new Rgb24(140, 140, 140);
        using var ms = new MemoryStream();
        img.SaveAsPng(ms);

        Assert.Null(CaptchaImagePreprocessor.GerarMascara(ms.ToArray()));
    }

    [Fact]
    public void Tensor_tem_tamanho_fixo_e_valores_entre_0_e_1()
    {
        var mascara = CaptchaImagePreprocessor.GerarMascara(ImagemSintetica())!;

        var tensor = CaptchaImagePreprocessor.ParaTensor(mascara);

        Assert.Equal(CaptchaImagePreprocessor.TensorLargura * CaptchaImagePreprocessor.TensorAltura, tensor.Length);
        Assert.All(tensor, v => Assert.InRange(v, 0f, 1f));
        Assert.True(tensor.Max() > 0.9f); // a letra preenche o recorte
    }

    [Fact]
    public void Variantes_para_o_tesseract_sao_geradas_em_png()
    {
        var variantes = CaptchaImagePreprocessor.GerarVariantes(ImagemSintetica());

        Assert.NotEmpty(variantes);
        Assert.All(variantes, v => Assert.Equal(0x89, v[0])); // assinatura PNG
    }
}

public class DecodificacaoCtcTests
{
    private const string Alfabeto = "abcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>Monta logits [T,1,37] em que cada passo tem um "vencedor" claro.</summary>
    private static DenseTensor<float> Logits(params int[] vencedores)
    {
        var t = new DenseTensor<float>([vencedores.Length, 1, Alfabeto.Length + 1]);
        for (var passo = 0; passo < vencedores.Length; passo++)
        {
            t[passo, 0, vencedores[passo]] = 10f;
        }

        return t;
    }

    private static int Indice(char c) => Alfabeto.IndexOf(c) + 1;

    [Fact]
    public void Colapsa_repeticoes_e_ignora_o_branco()
    {
        // b b _ b a a _ c d  => "bbacd"  (o branco entre dois 'b' mantém as duas letras)
        var leitura = RedeNeuralCaptchaSolver.DecodificarCtc(
            Logits(Indice('b'), Indice('b'), 0, Indice('b'), Indice('a'), Indice('a'), 0, Indice('c'), Indice('d')));

        Assert.Equal("bbacd", leitura.Texto);
        Assert.InRange(leitura.Confianca, 0.99, 1.0);
    }

    [Fact]
    public void Leitura_curta_demais_e_descartada()
    {
        var leitura = RedeNeuralCaptchaSolver.DecodificarCtc(Logits(Indice('a'), 0, Indice('b')));

        Assert.False(leitura.TemTexto);
        Assert.Equal(0, leitura.Confianca);
    }

    [Fact]
    public void Confianca_cai_quando_a_rede_esta_em_duvida()
    {
        var logits = Logits(Indice('a'), 0, Indice('b'), 0, Indice('c'), 0, Indice('d'));
        // Passo do 'c' passa a ser ambíguo entre 'c' e 'x'.
        logits[4, 0, Indice('x')] = 10f;

        var leitura = RedeNeuralCaptchaSolver.DecodificarCtc(logits);

        Assert.True(leitura.TemTexto);
        Assert.InRange(leitura.Confianca, 0.5, 0.95);
    }
}
