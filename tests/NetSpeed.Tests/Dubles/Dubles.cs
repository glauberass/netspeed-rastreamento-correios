using NetSpeed.Domain.Excecoes;
using NetSpeed.Domain.Modelos;
using NetSpeed.Domain.Portas;

namespace NetSpeed.Tests.Dubles;

/// <summary>Solver que devolve textos roteirizados, um por chamada (o último se repete).</summary>
internal sealed class SolverRoteirizado(params string[] textos) : ICaptchaSolver
{
    private int _chamadas;

    public int Chamadas => _chamadas;

    public LeituraCaptcha Resolver(byte[] imagem)
    {
        var texto = textos[Math.Min(_chamadas++, textos.Length - 1)];
        return texto.Length == 0 ? LeituraCaptcha.Vazia : new LeituraCaptcha(texto, Confianca);
    }

    public double Confianca { get; init; } = 1.0;
}

/// <summary>
/// Página falsa: aceita apenas o texto de CAPTCHA "correto"; devolve o HTML configurado no sucesso.
/// Permite testar o laço de tentativas e o pipeline inteiro sem navegador nem rede.
/// </summary>
internal sealed class PaginaFalsa : IPaginaRastreamento
{
    public string CaptchaCorreto { get; init; } = "abc12";

    public RespostaConsulta? RespostaFixa { get; init; }

    public Exception? FalharAoAbrir { get; init; }

    public Exception? FalharAoConsultar { get; init; }

    public string Html { get; init; } = "<ul><li class=\"step\"><div class=\"step-content\"><p class=\"text text-head\">Objeto postado. </p><p class=\"text text-content\">Recife - PE</p><p class=\"text text-content\">27/08/2026 15:30</p></div></li></ul>";

    public bool Aberta { get; private set; }

    public bool Descartada { get; private set; }

    public int NovasImagens { get; private set; }

    public int Consultas { get; private set; }

    public void Abrir()
    {
        if (FalharAoAbrir is not null)
        {
            throw FalharAoAbrir;
        }

        Aberta = true;
    }

    public byte[] ObterImagemCaptcha() => [1, 2, 3];

    public RespostaConsulta Consultar(string codigo, string textoCaptcha)
    {
        Consultas++;

        if (FalharAoConsultar is not null)
        {
            throw FalharAoConsultar;
        }

        if (RespostaFixa is not null)
        {
            return RespostaFixa;
        }

        return textoCaptcha == CaptchaCorreto
            ? new RespostaConsulta(TipoResposta.Sucesso)
            : new RespostaConsulta(TipoResposta.CaptchaInvalido, "Captcha inválido");
    }

    public void SolicitarNovaImagem() => NovasImagens++;

    public string ObterHtmlResultado() => Html;

    public void Dispose() => Descartada = true;
}

internal sealed class FabricaFalsa(Func<PaginaFalsa> criar) : IPaginaRastreamentoFactory
{
    public List<PaginaFalsa> Criadas { get; } = [];

    public IPaginaRastreamento Criar()
    {
        var pagina = criar();
        Criadas.Add(pagina);
        return pagina;
    }
}

internal static class Erros
{
    public static RastreamentoException Transitorio() =>
        new(CodigoErro.PortalIndisponivel, "portal fora do ar");
}
