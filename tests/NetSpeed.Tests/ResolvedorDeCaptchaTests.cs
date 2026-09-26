using Microsoft.Extensions.Logging.Abstractions;
using NetSpeed.Captcha;
using NetSpeed.Domain.Configuracao;
using NetSpeed.Domain.Excecoes;
using NetSpeed.Domain.Modelos;
using NetSpeed.Domain.Portas;
using NetSpeed.Tests.Dubles;

namespace NetSpeed.Tests;

public class ResolvedorDeCaptchaTests
{
    private static ResolvedorDeCaptchaNoPortal Criar(ICaptchaSolver solver, int maxTentativas = 5) =>
        new(solver, new CaptchaSettings { MaxTentativas = maxTentativas, IntervaloEntreTentativasMs = 0 }, NullLogger<ResolvedorDeCaptchaNoPortal>.Instance);

    private static RastreamentoResultado NovoResultado() => new() { CodigoObjeto = "NN437873753BR" };

    [Fact]
    public void Acerta_na_primeira_tentativa_e_registra_uma_tentativa_aceita()
    {
        var pagina = new PaginaFalsa { CaptchaCorreto = "abc12" };
        var resultado = NovoResultado();

        Criar(new SolverRoteirizado("abc12")).ResolverEConsultar(pagina, resultado.CodigoObjeto, resultado);

        var tentativa = Assert.Single(resultado.TentativasCaptcha);
        Assert.True(tentativa.Aceito);
        Assert.Equal("abc12", tentativa.Texto);
        Assert.Equal(1, tentativa.Numero);
    }

    [Fact]
    public void Repete_com_nova_imagem_ate_acertar_e_registra_todas_as_tentativas()
    {
        var pagina = new PaginaFalsa { CaptchaCorreto = "abc12" };
        var resultado = NovoResultado();

        Criar(new SolverRoteirizado("xxxxx", "yyyyy", "abc12")).ResolverEConsultar(pagina, resultado.CodigoObjeto, resultado);

        Assert.Equal([false, false, true], resultado.TentativasCaptcha.Select(t => t.Aceito));
        Assert.Equal([1, 2, 3], resultado.TentativasCaptcha.Select(t => t.Numero));
        Assert.Equal(3, pagina.Consultas);
    }

    [Fact]
    public void Solver_sem_leitura_renova_a_imagem_sem_consultar_o_portal()
    {
        var pagina = new PaginaFalsa { CaptchaCorreto = "abc12" };
        var resultado = NovoResultado();

        Criar(new SolverRoteirizado("", "abc12")).ResolverEConsultar(pagina, resultado.CodigoObjeto, resultado);

        // A leitura vazia não gasta uma requisição de consulta (que conta no limite do portal).
        Assert.Equal(1, pagina.Consultas);
        Assert.Equal(1, pagina.NovasImagens);
        Assert.Equal("solver não reconheceu texto plausível", resultado.TentativasCaptcha[0].Observacao);
    }

    [Fact]
    public void Esgotadas_as_tentativas_lanca_captcha_nao_resolvido()
    {
        var pagina = new PaginaFalsa { CaptchaCorreto = "abc12" };
        var resultado = NovoResultado();

        var ex = Assert.Throws<RastreamentoException>(
            () => Criar(new SolverRoteirizado("errado"), maxTentativas: 3)
                .ResolverEConsultar(pagina, resultado.CodigoObjeto, resultado));

        Assert.Equal(CodigoErro.CaptchaNaoResolvido, ex.Codigo);
        Assert.Equal(3, resultado.TentativasCaptcha.Count);
        Assert.All(resultado.TentativasCaptcha, t => Assert.False(t.Aceito));
    }

    [Fact]
    public void Erro_do_portal_com_captcha_aceito_e_objeto_nao_encontrado()
    {
        var pagina = new PaginaFalsa
        {
            RespostaFixa = new RespostaConsulta(TipoResposta.Erro, "Objeto não encontrado")
        };
        var resultado = NovoResultado();

        var ex = Assert.Throws<RastreamentoException>(
            () => Criar(new SolverRoteirizado("abc12")).ResolverEConsultar(pagina, resultado.CodigoObjeto, resultado));

        Assert.Equal(CodigoErro.ObjetoNaoEncontrado, ex.Codigo);
        Assert.Contains("Objeto não encontrado", ex.Message);
        // O CAPTCHA passou: a tentativa é registrada como aceita, e não se tenta de novo.
        Assert.True(Assert.Single(resultado.TentativasCaptcha).Aceito);
    }

    [Fact]
    public void Cancelamento_interrompe_o_laco()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var resultado = NovoResultado();

        Assert.Throws<OperationCanceledException>(
            () => Criar(new SolverRoteirizado("abc12"))
                .ResolverEConsultar(new PaginaFalsa(), resultado.CodigoObjeto, resultado, cts.Token));
    }

    [Fact]
    public void Numeracao_das_tentativas_continua_quando_o_resultado_e_reaproveitado_no_retry()
    {
        var resultado = NovoResultado();
        var resolvedor = Criar(new SolverRoteirizado("errado", "abc12"), maxTentativas: 1);

        // 1ª sessão do navegador: uma tentativa e o orçamento (1) acaba.
        Assert.Throws<RastreamentoException>(
            () => resolvedor.ResolverEConsultar(new PaginaFalsa { CaptchaCorreto = "abc12" }, resultado.CodigoObjeto, resultado));

        // 2ª sessão (retry): a numeração segue de onde parou.
        resolvedor.ResolverEConsultar(new PaginaFalsa { CaptchaCorreto = "abc12" }, resultado.CodigoObjeto, resultado);

        Assert.Equal([1, 2], resultado.TentativasCaptcha.Select(t => t.Numero));
    }

    [Fact]
    public void Portal_sem_resposta_registra_a_tentativa_com_a_causa_e_propaga_a_falha()
    {
        var pagina = new PaginaFalsa { FalharAoConsultar = new RastreamentoException(CodigoErro.Timeout, "sem resposta") };
        var resultado = NovoResultado();

        var ex = Assert.Throws<RastreamentoException>(
            () => Criar(new SolverRoteirizado("abc12")).ResolverEConsultar(pagina, resultado.CodigoObjeto, resultado));

        Assert.Equal(CodigoErro.Timeout, ex.Codigo);
        var tentativa = Assert.Single(resultado.TentativasCaptcha);
        Assert.False(tentativa.Aceito);
        Assert.Contains("Timeout", tentativa.Observacao);
    }

    [Fact]
    public void Leitura_abaixo_da_confianca_minima_e_descartada_sem_consultar_o_portal()
    {
        var pagina = new PaginaFalsa { CaptchaCorreto = "abc12" };
        var resultado = NovoResultado();
        var resolvedor = new ResolvedorDeCaptchaNoPortal(
            new SolverRoteirizado("duvid", "abc12") { Confianca = 0.4 },
            new CaptchaSettings { MaxTentativas = 3, ConfiancaMinima = 0.7, IntervaloEntreTentativasMs = 0 },
            NullLogger<ResolvedorDeCaptchaNoPortal>.Instance);

        var ex = Assert.Throws<RastreamentoException>(
            () => resolvedor.ResolverEConsultar(pagina, resultado.CodigoObjeto, resultado));

        // Nenhuma leitura chegou à confiança mínima: o portal nunca foi consultado.
        Assert.Equal(CodigoErro.CaptchaNaoResolvido, ex.Codigo);
        Assert.Equal(0, pagina.Consultas);
        Assert.Equal(3, pagina.NovasImagens);
        Assert.All(resultado.TentativasCaptcha, t => Assert.Contains("abaixo do mínimo", t.Observacao));
    }
}
