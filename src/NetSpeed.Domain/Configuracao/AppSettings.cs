namespace NetSpeed.Domain.Configuracao;

/// <summary>Configuração da aplicação, ligada à seção <c>AppSettings</c> do appsettings.json.</summary>
public sealed class AppSettings
{
    public LogSettings Log { get; set; } = new();

    public CorreiosSettings Correios { get; set; } = new();

    public NavegadorSettings Navegador { get; set; } = new();

    public CaptchaSettings Captcha { get; set; } = new();

    public RetrySettings Retry { get; set; } = new();

    public SaidaSettings Saida { get; set; } = new();

    public LoteSettings Lote { get; set; } = new();
}

public sealed class LoteSettings
{
    /// <summary>
    /// Consultas simultâneas. O padrão é 1: o portal aplica limite de requisições (HTTP 429) e
    /// cada consulta abre um navegador; aumentar só compensa se o limite permitir.
    /// </summary>
    public int MaxParalelismo { get; set; } = 1;

    /// <summary>Pausa antes de cada consulta do lote (exceto a primeira), para não estourar o limite do portal.</summary>
    public int IntervaloEntreConsultasMs { get; set; } = 1500;
}

public sealed class LogSettings
{
    public string Diretorio { get; set; } = "logs";

    public int ArquivosRetidos { get; set; } = 30;
}

public sealed class CorreiosSettings
{
    public string UrlPortal { get; set; } = "https://rastreamento.correios.com.br/app/index.php";

    /// <summary>Espera máxima por um elemento/resultado da página.</summary>
    public int TimeoutElementoSegundos { get; set; } = 20;

    /// <summary>Espera máxima pelo carregamento da página.</summary>
    public int TimeoutPaginaSegundos { get; set; } = 40;
}

public sealed class NavegadorSettings
{
    public bool Headless { get; set; } = true;

    public int LarguraJanela { get; set; } = 1366;

    public int AlturaJanela { get; set; } = 900;
}

public sealed class CaptchaSettings
{
    /// <summary>Quantas imagens diferentes tentar antes de desistir.</summary>
    public int MaxTentativas { get; set; } = 15;

    /// <summary>Pasta com <c>eng.traineddata</c> (fallback Tesseract); relativa ao diretório da aplicação.</summary>
    public string DiretorioTessdata { get; set; } = "tessdata";

    /// <summary>Modelo ONNX da rede neural (solver principal); relativo ao diretório da aplicação.</summary>
    public string ArquivoModelo { get; set; } = "modelos/captcha-crnn.onnx";

    /// <summary>
    /// Confiança mínima (0 a 1) para submeter uma leitura ao portal. Abaixo disso a imagem é
    /// descartada e outra é pedida — sem gastar a consulta, que conta no limite de requisições.
    /// </summary>
    public double ConfiancaMinima { get; set; } = 0.0;

    /// <summary>
    /// Pausa entre uma tentativa e a seguinte. O portal limita requisições por IP; tentativas em
    /// rajada (2 requisições por segundo) foram vistas provocando lentidão/bloqueio.
    /// </summary>
    public int IntervaloEntreTentativasMs { get; set; } = 1500;

    /// <summary>Se preenchido, grava cada imagem tentada (útil para depurar e calibrar).</summary>
    public string? DiretorioDebug { get; set; }
}

public sealed class RetrySettings
{
    /// <summary>Tentativas da consulta inteira diante de falha transitória (portal fora, timeout).</summary>
    public int MaxTentativasConsulta { get; set; } = 3;

    public int EsperaInicialMs { get; set; } = 5000;

    /// <summary>Fator do backoff exponencial entre tentativas.</summary>
    public double FatorBackoff { get; set; } = 2.0;
}

public sealed class SaidaSettings
{
    public string DiretorioJson { get; set; } = "saida";
}
