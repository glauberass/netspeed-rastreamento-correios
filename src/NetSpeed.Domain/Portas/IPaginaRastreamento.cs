namespace NetSpeed.Domain.Portas;

/// <summary>O que o portal respondeu a um "Consultar".</summary>
public enum TipoResposta
{
    /// <summary>A área de resultado foi renderizada com os eventos.</summary>
    Sucesso,

    /// <summary>O portal recusou o texto do CAPTCHA ("Captcha inválido").</summary>
    CaptchaInvalido,

    /// <summary>O portal mostrou outra mensagem de erro (ex.: objeto não encontrado).</summary>
    Erro
}

/// <param name="Tipo">Classificação da resposta.</param>
/// <param name="Mensagem">Mensagem exibida pelo portal, quando houver.</param>
public sealed record RespostaConsulta(TipoResposta Tipo, string? Mensagem = null);

/// <summary>
/// A página de rastreamento vista como uma API: tudo que a automação precisa fazer nela, sem
/// vazar Selenium para o restante do sistema. É a fronteira "navegação"; o que vem depois
/// (interpretar o HTML) não passa por aqui.
/// </summary>
public interface IPaginaRastreamento : IDisposable
{
    /// <summary>Abre o navegador e carrega o portal.</summary>
    void Abrir();

    /// <summary>Bytes PNG da imagem de CAPTCHA que está exibida agora.</summary>
    byte[] ObterImagemCaptcha();

    /// <summary>Preenche código e CAPTCHA, aciona "Consultar" e aguarda a resposta do portal.</summary>
    RespostaConsulta Consultar(string codigo, string textoCaptcha);

    /// <summary>Pede outra imagem de CAPTCHA (o portal já renova sozinho após cada consulta).</summary>
    void SolicitarNovaImagem();

    /// <summary>HTML da área de resultado, depois de expandir "mais informações".</summary>
    string ObterHtmlResultado();
}

/// <summary>Cria uma página (uma sessão de navegador) por consulta.</summary>
public interface IPaginaRastreamentoFactory
{
    IPaginaRastreamento Criar();
}
