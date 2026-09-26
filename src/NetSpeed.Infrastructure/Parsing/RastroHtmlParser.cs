using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using NetSpeed.Domain.Excecoes;
using NetSpeed.Domain.Modelos;

namespace NetSpeed.Infrastructure.Parsing;

/// <summary>Dados extraídos da área de resultado do portal.</summary>
public sealed record RastroParseado(
    string? TipoPostal,
    string? PrevisaoEntrega,
    IReadOnlyList<EventoRastreamento> Eventos);

/// <summary>
/// Converte o HTML da área de resultado (<c>#tabs-rastreamento</c>) em eventos.
/// </summary>
/// <remarks>
/// <para>
/// <b>Separado da navegação de propósito:</b> este parser só recebe texto HTML. Não sabe que
/// existe Selenium, então é testado com fixtures salvas do portal, sem abrir navegador nem
/// passar por CAPTCHA.
/// </para>
/// <para>
/// <b>Estrutura do portal</b> (montada por <c>rastroUnico.js</c>): cada evento é um
/// <c>li.step</c> com um <c>div.step-content</c> contendo, em ordem, <c>p.text-head</c> (o
/// status, com o prazo de retirada quando houver), um ou mais <c>p.text-content</c> (o local — ou,
/// nas transferências, "de …" e "para …"), <c>p.text-head</c> extras (mensagens adicionais) e,
/// por último, <c>p.text-content</c> com a data <c>dd/MM/yyyy HH:mm</c>. Quando há mais de três
/// eventos o portal esconde o miolo e mantém a lista completa em <c>#ver-rastro-unico</c>; este
/// parser prefere essa lista, que é a que traz todos os eventos.
/// </para>
/// </remarks>
public static partial class RastroHtmlParser
{
    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("pt-BR");

    public static RastroParseado Parse(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            throw new RastreamentoException(CodigoErro.LayoutAlterado, "A área de resultado veio vazia.");
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // A lista completa, quando existe, está em #ver-rastro-unico; senão, a lista única.
        var itens = doc.DocumentNode.SelectNodes("//*[@id='ver-rastro-unico']//li[contains(@class,'step')]")
                    ?? doc.DocumentNode.SelectNodes("//li[contains(@class,'step')]");

        if (itens is null || itens.Count == 0)
        {
            throw new RastreamentoException(
                CodigoErro.LayoutAlterado,
                "Nenhum evento (li.step) encontrado na área de resultado.");
        }

        var eventos = itens.Select(ParseEvento).ToList();

        return new RastroParseado(
            TipoPostal: Texto(doc.DocumentNode.SelectSingleNode("//*[contains(@class,'cabecalho-content')]/p[contains(@class,'text-content')]")),
            PrevisaoEntrega: ParsePrevisao(doc),
            Eventos: eventos);
    }

    private static EventoRastreamento ParseEvento(HtmlNode li)
    {
        var conteudo = li.SelectSingleNode(".//*[contains(@class,'step-content')]")
            ?? throw new RastreamentoException(
                CodigoErro.LayoutAlterado,
                "Evento sem div.step-content: o layout do portal mudou.");

        var paragrafos = (conteudo.SelectNodes("./p") ?? new HtmlNodeCollection(conteudo))
            .Select(p => (Cabecalho: p.HasClass("text-head"), Texto: Texto(p)))
            .Where(p => p.Texto is not null)
            .Select(p => (p.Cabecalho, Texto: p.Texto!))
            .ToList();

        if (paragrafos.Count == 0)
        {
            throw new RastreamentoException(
                CodigoErro.LayoutAlterado,
                "Evento sem nenhum texto: o layout do portal mudou.");
        }

        // Status = primeiro parágrafo; a data é o último, se casar com o padrão.
        var informacoes = new List<string>();
        var status = LimparStatus(paragrafos[0].Texto, informacoes);
        paragrafos.RemoveAt(0);

        string? dataOriginal = null;
        DateTime? dataHora = null;
        if (paragrafos.Count > 0 && DataHoraRegex().IsMatch(paragrafos[^1].Texto))
        {
            dataOriginal = paragrafos[^1].Texto;
            dataHora = ParseDataHora(dataOriginal);
            paragrafos.RemoveAt(paragrafos.Count - 1);
        }

        var locais = paragrafos.Where(p => !p.Cabecalho).Select(p => p.Texto).ToList();
        var detalhes = paragrafos.Where(p => p.Cabecalho).Select(p => p.Texto).ToList();

        var (local, origem, destino) = ClassificarLocais(locais, informacoes);

        var descricao = detalhes.Count > 0 ? string.Join(" ", detalhes) : null;

        return new EventoRastreamento
        {
            Status = status,
            Descricao = descricao,
            DataHora = dataHora,
            DataHoraOriginal = dataOriginal,
            Local = local,
            Origem = origem,
            Destino = destino,
            InformacoesAdicionais = informacoes
        };
    }

    /// <summary>
    /// Regras do portal: transferência traz "de X" e "para Y"; entrega traz "Pela X, cidade";
    /// demais eventos trazem apenas o local. Linhas que não se encaixam vão para informações
    /// adicionais — nada é descartado.
    /// </summary>
    private static (string? Local, string? Origem, string? Destino) ClassificarLocais(
        List<string> locais,
        List<string> informacoes)
    {
        string? origem = null, destino = null, local = null;

        foreach (var texto in locais)
        {
            if (origem is null && texto.StartsWith("de ", StringComparison.OrdinalIgnoreCase))
            {
                origem = texto[3..].Trim();
            }
            else if (destino is null && texto.StartsWith("para ", StringComparison.OrdinalIgnoreCase))
            {
                destino = texto[5..].Trim();
            }
            else if (local is null)
            {
                local = texto.StartsWith("Pela ", StringComparison.OrdinalIgnoreCase)
                    ? texto[5..].Trim()
                    : texto;
            }
            else
            {
                informacoes.Add(texto);
            }
        }

        return (local, origem, destino);
    }

    /// <summary>O portal monta "descrição. prazo": separa o prazo de retirada e tira o ponto final.</summary>
    private static string LimparStatus(string texto, List<string> informacoes)
    {
        var prazo = PrazoRetiradaRegex().Match(texto);
        if (prazo.Success)
        {
            informacoes.Add(prazo.Value.Trim().TrimEnd('.'));
            texto = texto.Remove(prazo.Index, prazo.Length).Trim();
        }

        return texto.TrimEnd('.', ' ');
    }

    private static string? ParsePrevisao(HtmlDocument doc)
    {
        var no = doc.DocumentNode.SelectNodes("//*[contains(@class,'cabecalho-content')]/p")?
            .FirstOrDefault(p => Texto(p)?.StartsWith("Previsão de Entrega", StringComparison.OrdinalIgnoreCase) == true);

        var texto = Texto(no);
        return texto is null ? null : texto["Previsão de Entrega:".Length..].Trim();
    }

    private static DateTime? ParseDataHora(string texto) =>
        DateTime.TryParseExact(texto, "dd/MM/yyyy HH:mm", Cultura, DateTimeStyles.None, out var data)
            ? data
            : null; // "00/00/0000 00:00" (portal sem data) vira null; o original é preservado.

    /// <summary>Texto visível do nó: <c>&lt;br&gt;</c> vira vírgula, espaços são normalizados.</summary>
    private static string? Texto(HtmlNode? no)
    {
        if (no is null)
        {
            return null;
        }

        foreach (var br in no.SelectNodes(".//br")?.ToList() ?? [])
        {
            br.ParentNode.ReplaceChild(HtmlNode.CreateNode(", "), br);
        }

        var texto = EspacosRegex().Replace(HtmlEntity.DeEntitize(no.InnerText), " ").Trim();
        texto = Regex.Replace(texto, @"\s*,(\s*,)+", ",").Trim(',', ' ');

        return texto.Length == 0 ? null : texto;
    }

    [GeneratedRegex(@"^\d{2}/\d{2}/\d{4} \d{2}:\d{2}$")]
    private static partial Regex DataHoraRegex();

    [GeneratedRegex(@"O prazo limite de retirada:\s*[\d/]+\.?", RegexOptions.IgnoreCase)]
    private static partial Regex PrazoRetiradaRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex EspacosRegex();
}
