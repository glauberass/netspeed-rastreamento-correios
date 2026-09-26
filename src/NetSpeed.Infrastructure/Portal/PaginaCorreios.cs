using Microsoft.Extensions.Logging;
using NetSpeed.Domain.Configuracao;
using NetSpeed.Domain.Excecoes;
using NetSpeed.Domain.Modelos;
using NetSpeed.Domain.Portas;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;

namespace NetSpeed.Infrastructure.Portal;

/// <summary>
/// Página de rastreamento dos Correios controlada por Selenium/Chrome.
/// </summary>
/// <remarks>
/// <para>
/// <b>Como os elementos são localizados.</b> Por <c>id</c> estável (<c>#objeto</c>,
/// <c>#captcha</c>, <c>#captcha_image</c>, <c>#b-pesquisar</c>, <c>#captcha_refresh_btn</c>,
/// <c>#tabs-rastreamento</c>), que é o que o próprio JavaScript do portal usa. Nada de XPath
/// posicional: se um id sumir, a falha é explícita (<see cref="CodigoErro.LayoutAlterado"/>) em
/// vez de clicar no elemento errado.
/// </para>
/// <para>
/// <b>Como a imagem do CAPTCHA é obtida.</b> O portal vincula a imagem à sessão (cookie): baixar
/// a URL por fora entregaria <em>outra</em> imagem, que não valeria para esta sessão. Por isso a
/// imagem exibida é copiada do próprio DOM para um <c>canvas</c> e exportada em PNG — o pixel
/// exato que o usuário veria, sem re-download nem screenshot (que sofreria com escala/DPI).
/// </para>
/// <para>
/// <b>Como a resposta é detectada.</b> O portal responde de três formas: renderiza
/// <c>li.step</c> em <c>#tabs-rastreamento</c> (sucesso); marca <c>#captcha</c> com a classe
/// <c>invalid</c> (CAPTCHA recusado); ou abre <c>#alerta</c> com a mensagem. "Buscando..." também
/// aparece em <c>#alerta</c>, mas só enquanto carrega, e é ignorado.
/// </para>
/// </remarks>
public sealed class PaginaCorreios : IPaginaRastreamento
{
    private const string MensagemBuscando = "Buscando";

    private readonly CorreiosSettings _correios;
    private readonly NavegadorSettings _navegador;
    private readonly ILogger<PaginaCorreios> _logger;

    private ChromeDriver? _driver;

    public PaginaCorreios(AppSettings settings, ILogger<PaginaCorreios> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _correios = settings.Correios;
        _navegador = settings.Navegador;
        _logger = logger;
    }

    public void Abrir()
    {
        try
        {
            _driver = CriarDriver();
            _driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(_correios.TimeoutPaginaSegundos);
            _driver.Navigate().GoToUrl(_correios.UrlPortal);

            VerificarBloqueioDoPortal();
            AguardarElemento(By.Id("captcha_image"));
            AguardarImagemCarregada();
            _logger.LogInformation("Portal carregado: {Url}", _correios.UrlPortal);
        }
        catch (WebDriverTimeoutException ex)
        {
            throw new RastreamentoException(
                CodigoErro.Timeout,
                $"O portal não carregou em {_correios.TimeoutPaginaSegundos}s.", ex);
        }
        catch (WebDriverException ex) when (ex is not NoSuchElementException)
        {
            throw new RastreamentoException(
                CodigoErro.PortalIndisponivel,
                $"Não foi possível abrir o portal: {ResumoErro(ex)}", ex);
        }
    }

    public byte[] ObterImagemCaptcha()
    {
        var driver = Driver;
        AguardarImagemCarregada();

        // O canvas exporta os pixels exatos da imagem exibida (mesma origem, sem taint).
        const string script = """
            const img = document.getElementById('captcha_image');
            const c = document.createElement('canvas');
            c.width = img.naturalWidth; c.height = img.naturalHeight;
            c.getContext('2d').drawImage(img, 0, 0);
            return c.toDataURL('image/png');
            """;

        var dataUrl = (string?)((IJavaScriptExecutor)driver).ExecuteScript(script);
        const string prefixo = "base64,";
        var indice = dataUrl?.IndexOf(prefixo, StringComparison.Ordinal) ?? -1;

        if (indice < 0)
        {
            throw new RastreamentoException(
                CodigoErro.LayoutAlterado,
                "Não foi possível extrair a imagem do CAPTCHA da página.");
        }

        return Convert.FromBase64String(dataUrl![(indice + prefixo.Length)..]);
    }

    public RespostaConsulta Consultar(string codigo, string textoCaptcha)
    {
        var driver = Driver;

        try
        {
            var srcAntes = ImagemSrc();

            Preencher(By.Id("objeto"), codigo);
            Preencher(By.Id("captcha"), textoCaptcha);
            driver.FindElement(By.Id("b-pesquisar")).Click();

            var resposta = AguardarResposta();

            // Após uma consulta o portal renova o CAPTCHA sozinho; garantimos que a imagem
            // atual já é a nova antes de devolver o controle.
            if (resposta.Tipo != TipoResposta.Sucesso)
            {
                AguardarImagemTrocar(srcAntes);
            }

            return resposta;
        }
        catch (WebDriverTimeoutException ex)
        {
            throw new RastreamentoException(
                CodigoErro.Timeout,
                $"O portal não respondeu à consulta em {_correios.TimeoutElementoSegundos}s.", ex);
        }
        catch (NoSuchElementException ex)
        {
            throw new RastreamentoException(
                CodigoErro.LayoutAlterado,
                $"Elemento esperado não encontrado na página: {ResumoErro(ex)}", ex);
        }
    }

    public void SolicitarNovaImagem()
    {
        var driver = Driver;
        var srcAntes = ImagemSrc();
        driver.FindElement(By.Id("captcha_refresh_btn")).Click();
        AguardarImagemTrocar(srcAntes);
    }

    public string ObterHtmlResultado()
    {
        var driver = Driver;

        // "Mais informações" (quando há mais de 3 eventos) alterna para a lista completa.
        var verMais = driver.FindElements(By.Id("a-ver-mais")).FirstOrDefault();
        if (verMais is { Displayed: true })
        {
            verMais.Click();
        }

        var area = driver.FindElement(By.Id("tabs-rastreamento"));
        return area.GetAttribute("innerHTML") ?? string.Empty;
    }

    public void Dispose()
    {
        if (_driver is null)
        {
            return;
        }

        try
        {
            _driver.Quit();
        }
#pragma warning disable CA1031 // Encerrar o navegador nunca pode mascarar o resultado da consulta.
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao encerrar o Chrome");
        }
#pragma warning restore CA1031
        finally
        {
            _driver.Dispose();
            _driver = null;
        }
    }

    /// <summary>
    /// O balanceador do portal responde uma página simples ("too many requests") quando o IP passa
    /// do limite. Sem esta checagem o robô esperaria o timeout por um elemento que nunca vai
    /// aparecer e reportaria "layout alterado" — diagnóstico errado. Aqui vira falha transitória
    /// com a causa real, e a política de retry espera antes de tentar de novo.
    /// </summary>
    private void VerificarBloqueioDoPortal()
    {
        var corpo = Driver.FindElements(By.TagName("body")).FirstOrDefault()?.Text ?? string.Empty;

        if (corpo.Contains("too many requests", StringComparison.OrdinalIgnoreCase) ||
            corpo.Contains("received too many", StringComparison.OrdinalIgnoreCase))
        {
            throw new RastreamentoException(
                CodigoErro.PortalIndisponivel,
                "O portal bloqueou temporariamente este IP por excesso de requisições (HTTP 429). Aguarde alguns minutos.");
        }
    }

    private ChromeDriver Driver =>
        _driver ?? throw new InvalidOperationException("A página não foi aberta (chame Abrir primeiro).");

    private ChromeDriver CriarDriver()
    {
        var options = new ChromeOptions();

        if (_navegador.Headless)
        {
            options.AddArgument("--headless=new");
        }

        options.AddArgument($"--window-size={_navegador.LarguraJanela},{_navegador.AlturaJanela}");
        options.AddArgument("--lang=pt-BR");
        options.AddArgument("--disable-gpu");
        options.AddArgument("--no-sandbox");
        options.AddArgument("--disable-dev-shm-usage");
        options.AddArgument("--disable-blink-features=AutomationControlled");
        options.AddExcludedArgument("enable-automation");
        options.PageLoadStrategy = PageLoadStrategy.Normal;

        var service = ChromeDriverService.CreateDefaultService();
        service.HideCommandPromptWindow = true;

        return new ChromeDriver(service, options, TimeSpan.FromSeconds(_correios.TimeoutPaginaSegundos));
    }

    private WebDriverWait Espera(int? segundos = null) =>
        new(new SystemClock(), Driver, TimeSpan.FromSeconds(segundos ?? _correios.TimeoutElementoSegundos), TimeSpan.FromMilliseconds(200))
        {
            Message = "Tempo esgotado aguardando a página."
        };

    private void AguardarElemento(By by) =>
        Espera().Until(d => d.FindElements(by).Count > 0);

    private void AguardarImagemCarregada() =>
        Espera().Until(d => ((IJavaScriptExecutor)d).ExecuteScript(
            "const i=document.getElementById('captcha_image'); return !!i && i.complete && i.naturalWidth>0;") is true);

    private string ImagemSrc() =>
        (string?)((IJavaScriptExecutor)Driver).ExecuteScript(
            "return document.getElementById('captcha_image').getAttribute('src');") ?? string.Empty;

    private void AguardarImagemTrocar(string srcAnterior)
    {
        Espera().Until(d => ImagemSrc() != srcAnterior);
        AguardarImagemCarregada();
    }

    private void Preencher(By by, string valor)
    {
        var campo = Driver.FindElement(by);
        campo.Clear();
        campo.SendKeys(valor);
    }

    private RespostaConsulta AguardarResposta()
    {
        RespostaConsulta? resposta = null;

        Espera().Until(d =>
        {
            if (d.FindElements(By.CssSelector("#tabs-rastreamento li.step")).Count > 0)
            {
                resposta = new RespostaConsulta(TipoResposta.Sucesso);
                return true;
            }

            var captcha = d.FindElement(By.Id("captcha"));
            var mensagemCaptcha = d.FindElements(By.CssSelector(".campos.captcha .mensagem")).FirstOrDefault()?.Text?.Trim();
            if ((captcha.GetAttribute("class") ?? string.Empty).Contains("invalid", StringComparison.Ordinal) &&
                !string.IsNullOrEmpty(mensagemCaptcha))
            {
                resposta = new RespostaConsulta(TipoResposta.CaptchaInvalido, mensagemCaptcha);
                return true;
            }

            var alerta = d.FindElements(By.CssSelector("#alerta.aberto .msg")).FirstOrDefault();
            var texto = alerta?.GetAttribute("textContent")?.Trim();
            if (!string.IsNullOrEmpty(texto) &&
                !texto.StartsWith(MensagemBuscando, StringComparison.OrdinalIgnoreCase))
            {
                resposta = new RespostaConsulta(TipoResposta.Erro, texto);
                return true;
            }

            return false;
        });

        return resposta!;
    }

    private static string ResumoErro(Exception ex) => ex.Message.Split('\n')[0];
}
