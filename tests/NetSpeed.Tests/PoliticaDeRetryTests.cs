using Microsoft.Extensions.Logging.Abstractions;
using NetSpeed.Application.Servicos;
using NetSpeed.Domain.Configuracao;
using NetSpeed.Domain.Excecoes;
using NetSpeed.Domain.Modelos;
using NetSpeed.Tests.Dubles;

namespace NetSpeed.Tests;

public class PoliticaDeRetryTests
{
    private static (PoliticaDeRetry Politica, List<TimeSpan> Esperas) Criar(int max = 3, int inicialMs = 100, double fator = 2.0)
    {
        var esperas = new List<TimeSpan>();
        var politica = new PoliticaDeRetry(
            new RetrySettings { MaxTentativasConsulta = max, EsperaInicialMs = inicialMs, FatorBackoff = fator },
            NullLogger<PoliticaDeRetry>.Instance)
        {
            // Não dorme de verdade: só registra quanto esperaria.
            Esperar = (t, _) =>
            {
                esperas.Add(t);
                return Task.CompletedTask;
            }
        };

        return (politica, esperas);
    }

    private static bool Transitoria(Exception ex) => ex is RastreamentoException { Codigo: CodigoErro.PortalIndisponivel };

    [Fact]
    public async Task Sucesso_na_primeira_nao_espera()
    {
        var (politica, esperas) = Criar();

        var r = await politica.ExecutarAsync(_ => Task.FromResult(42), Transitoria, CancellationToken.None);

        Assert.Equal(42, r);
        Assert.Empty(esperas);
    }

    [Fact]
    public async Task Falha_transitoria_repete_com_backoff_exponencial()
    {
        var (politica, esperas) = Criar(max: 4, inicialMs: 100, fator: 2.0);
        var chamadas = 0;

        var r = await politica.ExecutarAsync(
            tentativa =>
            {
                chamadas++;
                return tentativa < 4 ? throw Erros.Transitorio() : Task.FromResult("ok");
            },
            Transitoria,
            CancellationToken.None);

        Assert.Equal("ok", r);
        Assert.Equal(4, chamadas);
        Assert.Equal([100, 200, 400], esperas.Select(e => (int)e.TotalMilliseconds));
    }

    [Fact]
    public async Task Esgotadas_as_tentativas_propaga_a_ultima_falha()
    {
        var (politica, esperas) = Criar(max: 3);
        var chamadas = 0;

        var ex = await Assert.ThrowsAsync<RastreamentoException>(() => politica.ExecutarAsync<int>(
            _ =>
            {
                chamadas++;
                throw Erros.Transitorio();
            },
            Transitoria,
            CancellationToken.None));

        Assert.Equal(CodigoErro.PortalIndisponivel, ex.Codigo);
        Assert.Equal(3, chamadas);
        Assert.Equal(2, esperas.Count);
    }

    [Fact]
    public async Task Falha_definitiva_nao_e_repetida()
    {
        var (politica, esperas) = Criar();
        var chamadas = 0;

        await Assert.ThrowsAsync<RastreamentoException>(() => politica.ExecutarAsync<int>(
            _ =>
            {
                chamadas++;
                throw new RastreamentoException(CodigoErro.ObjetoNaoEncontrado, "não existe");
            },
            Transitoria,
            CancellationToken.None));

        Assert.Equal(1, chamadas);
        Assert.Empty(esperas);
    }
}
