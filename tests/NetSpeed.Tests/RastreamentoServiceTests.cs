using Microsoft.Extensions.Logging.Abstractions;
using NetSpeed.Application.Servicos;
using NetSpeed.Captcha;
using NetSpeed.Domain.Configuracao;
using NetSpeed.Domain.Modelos;
using NetSpeed.Domain.Portas;
using NetSpeed.Tests.Dubles;

namespace NetSpeed.Tests;

/// <summary>
/// Testes do fluxo completo (serviço + pipeline + resolvedor + parser) sobre uma página falsa:
/// tudo, exceto o navegador e a rede.
/// </summary>
public class RastreamentoServiceTests
{
    private static (RastreamentoService Servico, FabricaFalsa Fabrica) Criar(
        Func<PaginaFalsa> pagina,
        SolverRoteirizado? solver = null,
        int maxTentativasConsulta = 3,
        int maxTentativasCaptcha = 5)
    {
        var settings = new AppSettings
        {
            Captcha = { MaxTentativas = maxTentativasCaptcha, IntervaloEntreTentativasMs = 0 },
            Retry = { MaxTentativasConsulta = maxTentativasConsulta, EsperaInicialMs = 1 },
            Lote = { IntervaloEntreConsultasMs = 0 }
        };
        var fabrica = new FabricaFalsa(pagina);
        var resolvedor = new ResolvedorDeCaptchaNoPortal(
            solver ?? new SolverRoteirizado("abc12"),
            settings.Captcha,
            NullLogger<ResolvedorDeCaptchaNoPortal>.Instance);
        var retry = new PoliticaDeRetry(settings.Retry, NullLogger<PoliticaDeRetry>.Instance)
        {
            Esperar = (_, _) => Task.CompletedTask
        };

        return (new RastreamentoService(settings, fabrica, resolvedor, retry, NullLogger<RastreamentoService>.Instance), fabrica);
    }

    [Fact]
    public async Task Consulta_com_sucesso_devolve_eventos_e_fecha_o_navegador()
    {
        var (servico, fabrica) = Criar(() => new PaginaFalsa());

        var r = await servico.ConsultarAsync("nn437873753br");

        Assert.True(r.Sucesso);
        Assert.Null(r.Erro);
        Assert.Equal("NN437873753BR", r.CodigoObjeto);
        Assert.Equal("Objeto postado", Assert.Single(r.Eventos).Status);
        Assert.True(Assert.Single(fabrica.Criadas).Descartada);
    }

    [Fact]
    public async Task Codigo_invalido_responde_sem_abrir_navegador()
    {
        var (servico, fabrica) = Criar(() => new PaginaFalsa());

        var r = await servico.ConsultarAsync("XX000");

        Assert.False(r.Sucesso);
        Assert.Equal(CodigoErro.CodigoInvalido, r.Erro!.Codigo);
        Assert.Empty(fabrica.Criadas);
    }

    [Fact]
    public async Task Captcha_nunca_resolvido_vira_erro_tipado_com_todas_as_tentativas_registradas()
    {
        var (servico, fabrica) = Criar(
            () => new PaginaFalsa { CaptchaCorreto = "certo" },
            new SolverRoteirizado("errado"),
            maxTentativasCaptcha: 4);

        var r = await servico.ConsultarAsync("NN437873753BR");

        Assert.False(r.Sucesso);
        Assert.Equal(CodigoErro.CaptchaNaoResolvido, r.Erro!.Codigo);
        Assert.Equal(4, r.TentativasCaptcha.Count);
        Assert.True(Assert.Single(fabrica.Criadas).Descartada);
    }

    [Fact]
    public async Task Objeto_inexistente_vira_erro_tipado_e_nao_repete()
    {
        var (servico, fabrica) = Criar(() => new PaginaFalsa
        {
            RespostaFixa = new RespostaConsulta(TipoResposta.Erro, "Objeto não encontrado")
        });

        var r = await servico.ConsultarAsync("NN437873753BR");

        Assert.Equal(CodigoErro.ObjetoNaoEncontrado, r.Erro!.Codigo);
        Assert.Single(fabrica.Criadas); // não é transitório: nada de nova sessão
    }

    [Fact]
    public async Task Portal_fora_do_ar_repete_com_navegador_novo_e_depois_funciona()
    {
        var abertas = 0;
        var (servico, fabrica) = Criar(() => ++abertas < 3
            ? new PaginaFalsa { FalharAoAbrir = Erros.Transitorio() }
            : new PaginaFalsa());

        var r = await servico.ConsultarAsync("NN437873753BR");

        Assert.True(r.Sucesso);
        Assert.Equal(3, fabrica.Criadas.Count);
        Assert.All(fabrica.Criadas, p => Assert.True(p.Descartada)); // nenhum navegador vaza
    }

    [Fact]
    public async Task Portal_fora_do_ar_em_todas_as_tentativas_vira_erro_tipado()
    {
        var (servico, fabrica) = Criar(
            () => new PaginaFalsa { FalharAoAbrir = Erros.Transitorio() },
            maxTentativasConsulta: 2);

        var r = await servico.ConsultarAsync("NN437873753BR");

        Assert.False(r.Sucesso);
        Assert.Equal(CodigoErro.PortalIndisponivel, r.Erro!.Codigo);
        Assert.Equal(2, fabrica.Criadas.Count);
    }

    [Fact]
    public async Task Layout_alterado_e_reportado_como_tal()
    {
        var (servico, _) = Criar(() => new PaginaFalsa { Html = "<div>layout novo</div>" });

        var r = await servico.ConsultarAsync("NN437873753BR");

        Assert.False(r.Sucesso);
        Assert.Equal(CodigoErro.LayoutAlterado, r.Erro!.Codigo);
    }

    [Fact]
    public async Task Falha_inesperada_nao_derruba_o_chamador()
    {
        var (servico, _) = Criar(() => new PaginaFalsa { FalharAoAbrir = new InvalidOperationException("boom") });

        var r = await servico.ConsultarAsync("NN437873753BR");

        Assert.False(r.Sucesso);
        Assert.Equal(CodigoErro.ErroInesperado, r.Erro!.Codigo);
        Assert.Contains("boom", r.Erro.Mensagem);
    }

    [Fact]
    public async Task Lote_consulta_todos_sem_repetir_codigos_e_um_erro_nao_afeta_os_outros()
    {
        var (servico, fabrica) = Criar(() => new PaginaFalsa());

        var resultados = await servico.ConsultarVariosAsync(
            ["NN437873753BR", "nn437873753br", "XX000", "AA123456789BR"]);

        // "NN437873753BR" e "nn437873753br" são o mesmo código: consultado uma vez só. O inválido
        // vira erro sem abrir navegador, e a ordem informada é preservada.
        Assert.Equal(["NN437873753BR", "XX000", "AA123456789BR"], resultados.Select(r => r.CodigoObjeto));
        Assert.Equal(CodigoErro.CodigoInvalido, resultados[1].Erro!.Codigo);
        Assert.Equal([true, false, true], resultados.Select(r => r.Sucesso));
        Assert.Equal(2, fabrica.Criadas.Count);
    }
}
