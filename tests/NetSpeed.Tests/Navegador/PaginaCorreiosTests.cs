using Microsoft.Extensions.Logging.Abstractions;
using NetSpeed.Application.Servicos;
using NetSpeed.Captcha;
using NetSpeed.Domain.Configuracao;
using NetSpeed.Domain.Excecoes;
using NetSpeed.Domain.Modelos;
using NetSpeed.Domain.Portas;
using NetSpeed.Infrastructure.Parsing;
using NetSpeed.Infrastructure.Portal;
using NetSpeed.Tests.Dubles;

namespace NetSpeed.Tests.Navegador;

/// <summary>
/// Fact que só roda com Chrome instalado e <c>NETSPEED_TESTE_NAVEGADOR=1</c>: abre um Chrome
/// de verdade (headless) contra o <see cref="PortalFalso"/>. Fica desligado por padrão para o
/// <c>dotnet test</c> comum ser rápido e funcionar offline.
/// </summary>
public sealed class FactNavegadorAttribute : FactAttribute
{
    public FactNavegadorAttribute()
    {
        if (Environment.GetEnvironmentVariable("NETSPEED_TESTE_NAVEGADOR") != "1")
        {
            Skip = "Teste com navegador real: defina NETSPEED_TESTE_NAVEGADOR=1 (exige Chrome instalado).";
        }
    }
}

public class PaginaCorreiosTests : IDisposable
{
    private readonly PortalFalso _portal = new();

    public void Dispose() => _portal.Dispose();

    private AppSettings Settings() => new()
    {
        Correios = { UrlPortal = _portal.UrlPortal, TimeoutElementoSegundos = 10, TimeoutPaginaSegundos = 15 },
        Navegador = { Headless = true },
        Captcha = { MaxTentativas = 5, IntervaloEntreTentativasMs = 0 },
        Retry = { MaxTentativasConsulta = 2, EsperaInicialMs = 1 }
    };

    private PaginaCorreios CriarPagina() => new(Settings(), NullLogger<PaginaCorreios>.Instance);

    [FactNavegador]
    public void Obtem_a_imagem_do_captcha_como_png_e_troca_ao_pedir_nova()
    {
        using var pagina = CriarPagina();
        pagina.Abrir();

        var primeira = pagina.ObterImagemCaptcha();
        pagina.SolicitarNovaImagem();
        var segunda = pagina.ObterImagemCaptcha();

        Assert.Equal(0x89, primeira[0]);           // assinatura PNG
        Assert.NotEqual(primeira, segunda);         // é outra imagem
    }

    [FactNavegador]
    public void Captcha_errado_e_reconhecido_e_o_certo_devolve_o_html_com_todos_os_eventos()
    {
        using var pagina = CriarPagina();
        pagina.Abrir();

        var errado = pagina.Consultar("NN437873753BR", "errado");
        var certo = pagina.Consultar("NN437873753BR", PortalFalso.CaptchaCorreto);

        Assert.Equal(TipoResposta.CaptchaInvalido, errado.Tipo);
        Assert.Equal(TipoResposta.Sucesso, certo.Tipo);

        // O "Mais informações" é expandido e o parser enxerga os 4 eventos, não só os 3 do resumo.
        var eventos = RastroHtmlParser.Parse(pagina.ObterHtmlResultado()).Eventos;
        Assert.Equal(4, eventos.Count);
    }

    [FactNavegador]
    public void Objeto_inexistente_volta_como_erro_do_portal_com_a_mensagem()
    {
        using var pagina = CriarPagina();
        pagina.Abrir();

        var resposta = pagina.Consultar(PortalFalso.CodigoInexistente, PortalFalso.CaptchaCorreto);

        Assert.Equal(TipoResposta.Erro, resposta.Tipo);
        Assert.Equal("Objeto não encontrado", resposta.Mensagem);
    }

    [FactNavegador]
    public void Portal_fora_do_ar_vira_falha_transitoria_classificada()
    {
        var settings = Settings();
        settings.Correios.UrlPortal = "http://localhost:1/app/index.php"; // ninguém escuta
        using var pagina = new PaginaCorreios(settings, NullLogger<PaginaCorreios>.Instance);

        var ex = Assert.Throws<RastreamentoException>(pagina.Abrir);

        Assert.Contains(ex.Codigo, new[] { CodigoErro.PortalIndisponivel, CodigoErro.Timeout });
    }

    [FactNavegador]
    public async Task Fluxo_completo_com_navegador_real_resolve_o_captcha_e_extrai_os_eventos()
    {
        var settings = Settings();
        var fabrica = new PaginaCorreiosFactory(settings, NullLoggerFactory.Instance);
        var solver = new SolverRoteirizado("errad", "outro", PortalFalso.CaptchaCorreto);
        var resolvedor = new ResolvedorDeCaptchaNoPortal(solver, settings.Captcha, NullLogger<ResolvedorDeCaptchaNoPortal>.Instance);
        var servico = new RastreamentoService(
            settings, fabrica, resolvedor,
            new PoliticaDeRetry(settings.Retry, NullLogger<PoliticaDeRetry>.Instance),
            NullLogger<RastreamentoService>.Instance);

        var r = await servico.ConsultarAsync("NN437873753BR");

        Assert.True(r.Sucesso, r.Erro?.Mensagem);
        Assert.Equal(4, r.Eventos.Count);
        Assert.Equal([false, false, true], r.TentativasCaptcha.Select(t => t.Aceito));
    }

    [FactNavegador]
    public async Task Fluxo_completo_com_objeto_inexistente_devolve_erro_tipado()
    {
        var settings = Settings();
        var solver = new SolverRoteirizado(PortalFalso.CaptchaCorreto);
        var servico = new RastreamentoService(
            settings,
            new PaginaCorreiosFactory(settings, NullLoggerFactory.Instance),
            new ResolvedorDeCaptchaNoPortal(solver, settings.Captcha, NullLogger<ResolvedorDeCaptchaNoPortal>.Instance),
            new PoliticaDeRetry(settings.Retry, NullLogger<PoliticaDeRetry>.Instance),
            NullLogger<RastreamentoService>.Instance);

        var r = await servico.ConsultarAsync(PortalFalso.CodigoInexistente);

        Assert.False(r.Sucesso);
        Assert.Equal(CodigoErro.ObjetoNaoEncontrado, r.Erro!.Codigo);
        Assert.Contains("não encontrado", r.Erro.Mensagem);
    }
}
