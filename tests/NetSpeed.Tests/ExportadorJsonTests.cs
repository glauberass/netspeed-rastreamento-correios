using System.Text.Json;
using NetSpeed.Domain.Configuracao;
using NetSpeed.Domain.Modelos;
using NetSpeed.Infrastructure.Saida;

namespace NetSpeed.Tests;

public class ExportadorJsonTests
{
    private static RastreamentoResultado Exemplo(string codigo = "NN437873753BR") => new()
    {
        CodigoObjeto = codigo,
        Sucesso = true,
        Eventos =
        [
            new EventoRastreamento
            {
                Status = "Objeto entregue ao destinatário",
                DataHora = new DateTime(2026, 8, 31, 16, 4, 0),
                Local = "São José do Rio Preto - SP"
            },
            new EventoRastreamento
            {
                Status = "Objeto em transferência - por favor aguarde",
                DataHora = new DateTime(2026, 8, 28, 8, 52, 0),
                Origem = "Unidade de Tratamento, São José do Rio Preto - SP",
                Destino = "Unidade de Distribuição, São José do Rio Preto - SP"
            }
        ]
    };

    [Fact]
    public void Json_segue_o_formato_de_referencia_do_enunciado()
    {
        var json = new ExportadorJson(new AppSettings()).Serializar(Exemplo());
        using var doc = JsonDocument.Parse(json);
        var raiz = doc.RootElement;

        Assert.Equal("NN437873753BR", raiz.GetProperty("codigoObjeto").GetString());
        var eventos = raiz.GetProperty("eventos");
        Assert.Equal(2, eventos.GetArrayLength());

        var entrega = eventos[0];
        Assert.Equal("Objeto entregue ao destinatário", entrega.GetProperty("status").GetString());
        Assert.Equal("2026-08-31T16:04:00", entrega.GetProperty("dataHora").GetString());
        Assert.Equal("São José do Rio Preto - SP", entrega.GetProperty("local").GetString());

        // Campos ausentes saem como null (esquema estável), não somem.
        Assert.Equal(JsonValueKind.Null, entrega.GetProperty("descricao").ValueKind);
        Assert.Equal(JsonValueKind.Null, entrega.GetProperty("origem").ValueKind);
        Assert.Equal(JsonValueKind.Null, entrega.GetProperty("destino").ValueKind);
    }

    [Fact]
    public void Acentos_saem_legiveis_e_nao_escapados()
    {
        var json = new ExportadorJson(new AppSettings()).Serializar(Exemplo());

        Assert.Contains("destinatário", json);
        Assert.DoesNotContain("\\u00", json);
    }

    [Fact]
    public void Erro_sai_com_codigo_em_texto()
    {
        var resultado = new RastreamentoResultado
        {
            CodigoObjeto = "NN437873753BR",
            Sucesso = false,
            Erro = new ErroRastreamento(CodigoErro.CaptchaNaoResolvido, "não resolveu")
        };
        resultado.TentativasCaptcha.Add(new TentativaCaptcha(1, "abc12", false, 350, "Captcha inválido"));

        using var doc = JsonDocument.Parse(new ExportadorJson(new AppSettings()).Serializar(resultado));

        Assert.Equal("captchaNaoResolvido", doc.RootElement.GetProperty("erro").GetProperty("codigo").GetString());
        Assert.Equal("abc12", doc.RootElement.GetProperty("tentativasCaptcha")[0].GetProperty("texto").GetString());
    }

    [Fact]
    public async Task Uma_consulta_grava_objeto_e_varias_gravam_array()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "netspeed-tests-" + Guid.NewGuid().ToString("N"));
        var exportador = new ExportadorJson(new AppSettings { Saida = { DiretorioJson = pasta } });

        try
        {
            var um = await exportador.ExportarAsync([Exemplo()], null);
            var varios = await exportador.ExportarAsync([Exemplo(), Exemplo("AA123456789BR")], Path.Combine(pasta, "lote.json"));

            Assert.EndsWith("NN437873753BR.json", um);
            Assert.Equal(JsonValueKind.Object, JsonDocument.Parse(await File.ReadAllTextAsync(um)).RootElement.ValueKind);
            Assert.Equal(JsonValueKind.Array, JsonDocument.Parse(await File.ReadAllTextAsync(varios)).RootElement.ValueKind);
        }
        finally
        {
            Directory.Delete(pasta, recursive: true);
        }
    }
}
