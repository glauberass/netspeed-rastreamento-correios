using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NetSpeed.Domain.Configuracao;
using NetSpeed.Domain.Excecoes;
using NetSpeed.Domain.Modelos;
using NetSpeed.Domain.Portas;

namespace NetSpeed.Captcha;

/// <summary>
/// Laço de tentativas do CAPTCHA: obter imagem, reconhecer, submeter, validar, renovar e repetir.
/// </summary>
/// <remarks>
/// <para>
/// O portal é o oráculo: só ele sabe se o texto está certo, e só descobrimos ao submeter. Como o
/// OCR erra uma fração das imagens, a robustez vem daqui — cada falha renova a imagem e tenta
/// de novo, até <see cref="CaptchaSettings.MaxTentativas"/>. Com acerto <c>p</c> por imagem, a
/// chance de resolver em N tentativas é <c>1 - (1 - p)^N</c>.
/// </para>
/// <para>
/// Toda tentativa é registrada em <see cref="RastreamentoResultado.TentativasCaptcha"/> (texto
/// lido, aceito ou não, duração) — o enunciado exige esse registro.
/// </para>
/// <para>
/// A classe depende só de <see cref="IPaginaRastreamento"/> e <see cref="ICaptchaSolver"/>:
/// testa-se com página e solver falsos, sem navegador e sem rede.
/// </para>
/// </remarks>
public sealed class ResolvedorDeCaptchaNoPortal(
    ICaptchaSolver solver,
    CaptchaSettings settings,
    ILogger<ResolvedorDeCaptchaNoPortal> logger)
{
    /// <summary>
    /// Resolve o CAPTCHA e consulta o objeto. Ao retornar sem exceção, o portal aceitou o CAPTCHA
    /// e a área de resultado está renderizada.
    /// </summary>
    /// <exception cref="RastreamentoException">
    /// <see cref="CodigoErro.CaptchaNaoResolvido"/> ao esgotar as tentativas;
    /// <see cref="CodigoErro.ObjetoNaoEncontrado"/> quando o CAPTCHA passou mas o portal recusou o objeto.
    /// </exception>
    public void ResolverEConsultar(
        IPaginaRastreamento pagina,
        string codigo,
        RastreamentoResultado resultado,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pagina);
        ArgumentNullException.ThrowIfNull(resultado);

        for (var n = 1; n <= settings.MaxTentativas; n++)
        {
            ct.ThrowIfCancellationRequested();

            // Espaça as tentativas (a primeira não espera): rajadas contra o portal provocam 429.
            if (n > 1 && settings.IntervaloEntreTentativasMs > 0)
            {
                ct.WaitHandle.WaitOne(settings.IntervaloEntreTentativasMs);
                ct.ThrowIfCancellationRequested();
            }

            var relogio = Stopwatch.StartNew();

            // Numeração contínua na consulta, inclusive se a sessão do navegador foi refeita (retry).
            var numero = resultado.TentativasCaptcha.Count + 1;

            var imagem = pagina.ObterImagemCaptcha();
            var leitura = solver.Resolver(imagem);
            var texto = leitura.Texto;

            if (!leitura.TemTexto)
            {
                Registrar(resultado, numero, texto, aceito: false, relogio, "solver não reconheceu texto plausível");
                SalvarParaDepuracao(imagem, codigo, numero, texto, "sem-leitura");
                pagina.SolicitarNovaImagem();
                continue;
            }

            if (leitura.Confianca < settings.ConfiancaMinima)
            {
                // Leitura pouco confiável: não vale gastar uma consulta (que conta no limite do portal).
                Registrar(resultado, numero, texto, aceito: false, relogio,
                    $"confiança {leitura.Confianca:P0} abaixo do mínimo {settings.ConfiancaMinima:P0}; imagem descartada");
                SalvarParaDepuracao(imagem, codigo, numero, texto, "descartada");
                pagina.SolicitarNovaImagem();
                continue;
            }

            RespostaConsulta resposta;
            try
            {
                resposta = pagina.Consultar(codigo, texto);
            }
            catch (RastreamentoException ex)
            {
                // O portal não respondeu: a tentativa fica registrada (com a causa) antes de a falha subir.
                Registrar(resultado, numero, texto, aceito: false, relogio, $"{ex.Codigo}: {ex.Message}");
                SalvarParaDepuracao(imagem, codigo, numero, texto, "sem-resposta");
                throw;
            }

            switch (resposta.Tipo)
            {
                case TipoResposta.Sucesso:
                    Registrar(resultado, numero, texto, aceito: true, relogio, null);
                    SalvarParaDepuracao(imagem, codigo, numero, texto, "ok");
                    return;

                case TipoResposta.CaptchaInvalido:
                    Registrar(resultado, numero, texto, aceito: false, relogio, resposta.Mensagem);
                    SalvarParaDepuracao(imagem, codigo, numero, texto, "x");
                    // Depois de "Consultar" o portal já renovou a imagem; não é preciso pedir outra.
                    break;

                default:
                    // O CAPTCHA foi aceito (senão a mensagem seria "Captcha inválido"); o erro é do objeto.
                    Registrar(resultado, numero, texto, aceito: true, relogio, resposta.Mensagem);
                    SalvarParaDepuracao(imagem, codigo, numero, texto, "ok");
                    throw new RastreamentoException(
                        CodigoErro.ObjetoNaoEncontrado,
                        resposta.Mensagem ?? "O portal recusou a consulta do objeto.");
            }
        }

        throw new RastreamentoException(
            CodigoErro.CaptchaNaoResolvido,
            $"O CAPTCHA não foi resolvido em {settings.MaxTentativas} tentativas.");
    }

    private void Registrar(
        RastreamentoResultado resultado,
        int numero,
        string texto,
        bool aceito,
        Stopwatch relogio,
        string? observacao)
    {
        var tentativa = new TentativaCaptcha(numero, texto, aceito, relogio.ElapsedMilliseconds, observacao);
        resultado.TentativasCaptcha.Add(tentativa);

        logger.LogInformation(
            "CAPTCHA tentativa {Numero}: '{Texto}' => {Situacao} ({Ms} ms){Observacao}",
            numero,
            texto,
            aceito ? "ACEITO" : "recusado",
            tentativa.DuracaoMs,
            observacao is null ? string.Empty : $" — {observacao}");
    }

    /// <summary>
    /// Grava a imagem tentada quando <c>DiretorioDebug</c> está configurado. O nome traz a leitura
    /// e o desfecho; as imagens <c>ok</c> têm rótulo confirmado pelo portal (úteis para retreinar).
    /// </summary>
    private void SalvarParaDepuracao(byte[] imagem, string codigo, int numero, string texto, string desfecho)
    {
        if (string.IsNullOrWhiteSpace(settings.DiretorioDebug))
        {
            return;
        }

        Directory.CreateDirectory(settings.DiretorioDebug);
        File.WriteAllBytes(
            Path.Combine(
                settings.DiretorioDebug,
                $"{codigo}_{DateTime.Now:HHmmss}_{numero:D2}_{(texto.Length == 0 ? "-" : texto)}_{desfecho}.png"),
            imagem);
    }
}
