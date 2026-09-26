using NetSpeed.Domain.Excecoes;
using NetSpeed.Domain.Modelos;
using NetSpeed.Infrastructure.Parsing;

namespace NetSpeed.Tests;

public class RastroHtmlParserTests
{
    private static string Fixture(string nome) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", nome));

    [Fact]
    public void Deve_ler_todos_os_eventos_da_lista_completa_e_nao_da_resumida()
    {
        // O portal mostra só 3 eventos (com "Mais informações") e mantém os 4 em #ver-rastro-unico.
        var rastro = RastroHtmlParser.Parse(Fixture("rastro-4-eventos.html"));

        Assert.Equal(4, rastro.Eventos.Count);
        Assert.Equal(
            ["Objeto entregue ao destinatário", "Objeto saiu para entrega ao destinatário",
             "Objeto em transferência - por favor aguarde", "Objeto postado"],
            rastro.Eventos.Select(e => e.Status));
    }

    [Fact]
    public void Evento_de_entrega_tem_local_e_nao_tem_origem_nem_destino()
    {
        var entrega = RastroHtmlParser.Parse(Fixture("rastro-4-eventos.html")).Eventos[0];

        Assert.Equal("Unidade de Distribuição, São José do Rio Preto - SP", entrega.Local);
        Assert.Null(entrega.Origem);
        Assert.Null(entrega.Destino);
        Assert.Null(entrega.Descricao);
        Assert.Equal(new DateTime(2026, 8, 31, 16, 4, 0), entrega.DataHora);
    }

    [Fact]
    public void Evento_de_transferencia_tem_origem_e_destino_e_nao_tem_local()
    {
        var transferencia = RastroHtmlParser.Parse(Fixture("rastro-4-eventos.html")).Eventos[2];

        Assert.Null(transferencia.Local);
        Assert.Equal("Unidade de Tratamento, São José do Rio Preto - SP", transferencia.Origem);
        Assert.Equal("Unidade de Distribuição, São José do Rio Preto - SP", transferencia.Destino);
        Assert.Equal(new DateTime(2026, 8, 28, 8, 52, 0), transferencia.DataHora);
    }

    [Fact]
    public void Mensagem_adicional_do_evento_vira_descricao()
    {
        var postagem = RastroHtmlParser.Parse(Fixture("rastro-4-eventos.html")).Eventos[3];

        Assert.Equal("Objeto postado", postagem.Status);
        Assert.Equal("Objeto postado após o horário limite da unidade.", postagem.Descricao);
        Assert.Equal("Recife - PE", postagem.Local);
    }

    [Fact]
    public void Deve_ler_tipo_postal_e_previsao_de_entrega()
    {
        var rastro = RastroHtmlParser.Parse(Fixture("rastro-4-eventos.html"));

        Assert.Equal("SEDEX", rastro.TipoPostal);
        Assert.Equal("31/08/2026", rastro.PrevisaoEntrega);
    }

    [Fact]
    public void Prazo_de_retirada_vai_para_informacoes_adicionais_e_data_zerada_vira_nula()
    {
        var evento = RastroHtmlParser.Parse(Fixture("rastro-1-evento-retirada.html")).Eventos.Single();

        Assert.Equal("Objeto aguardando retirada no endereço indicado", evento.Status);
        Assert.Contains("O prazo limite de retirada: 05/09/2026", evento.InformacoesAdicionais);
        Assert.Null(evento.DataHora);
        Assert.Equal("00/00/0000 00:00", evento.DataHoraOriginal);
        Assert.Equal("Agência Centro,Rua das Flores, 100, Centro, Recife - PE", evento.Local);
        Assert.Null(RastroHtmlParser.Parse(Fixture("rastro-1-evento-retirada.html")).TipoPostal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Html_vazio_e_layout_alterado(string html)
    {
        var ex = Assert.Throws<RastreamentoException>(() => RastroHtmlParser.Parse(html));

        Assert.Equal(CodigoErro.LayoutAlterado, ex.Codigo);
    }

    [Fact]
    public void Html_sem_eventos_e_layout_alterado()
    {
        var ex = Assert.Throws<RastreamentoException>(
            () => RastroHtmlParser.Parse("<div><p>nada por aqui</p></div>"));

        Assert.Equal(CodigoErro.LayoutAlterado, ex.Codigo);
    }

    [Fact]
    public void Evento_sem_step_content_e_layout_alterado()
    {
        var ex = Assert.Throws<RastreamentoException>(
            () => RastroHtmlParser.Parse("<ul><li class='step'><div class='novo-layout'>x</div></li></ul>"));

        Assert.Equal(CodigoErro.LayoutAlterado, ex.Codigo);
    }

    [Fact]
    public void Html_real_capturado_do_portal_gera_os_12_eventos_esperados()
    {
        // Fixture capturada do portal real (objeto de teste do enunciado, NN437873753BR).
        var rastro = RastroHtmlParser.Parse(Fixture("rastro-real-NN437873753BR.html"));

        Assert.Equal(12, rastro.Eventos.Count);
        Assert.Equal("PACKET STANDARD IMPORTAÇÃO", rastro.TipoPostal);
        Assert.Equal("Objeto entregue ao destinatário", rastro.Eventos[0].Status);
        Assert.Equal(new DateTime(2026, 8, 31, 16, 4, 0), rastro.Eventos[0].DataHora);

        var transferencia = rastro.Eventos[2];
        Assert.Equal("Objeto em transferência - por favor aguarde", transferencia.Status);
        Assert.Null(transferencia.Local);
        Assert.NotNull(transferencia.Origem);
        Assert.NotNull(transferencia.Destino);

        Assert.All(rastro.Eventos, e => Assert.NotNull(e.DataHora));
    }
}
