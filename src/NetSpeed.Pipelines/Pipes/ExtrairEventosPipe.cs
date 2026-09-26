using NetSpeed.Domain.Configuracao;
using NetSpeed.Domain.Modelos;
using NetSpeed.Domain.Portas;
using NetSpeed.Infrastructure.Parsing;
using PipeliningLibrary;

namespace NetSpeed.Pipelines.Pipes;

/// <summary>Lê o HTML do resultado e o converte em eventos. A navegação termina no HTML; daqui em diante é só texto.</summary>
public sealed class ExtrairEventosPipe : IPipe
{
    public object Run(dynamic input)
    {
        IPaginaRastreamento pagina = input.Pagina;
        RastreamentoResultado resultado = input.Resultado;

        var html = pagina.ObterHtmlResultado();

        // Com o diretório de depuração ligado, o HTML cru fica salvo: é o que permite reproduzir um
        // problema de extração (ou criar uma fixture de teste) sem consultar o portal de novo.
        AppSettings settings = input.Settings;
        if (!string.IsNullOrWhiteSpace(settings.Captcha.DiretorioDebug))
        {
            Directory.CreateDirectory(settings.Captcha.DiretorioDebug);
            File.WriteAllText(
                Path.Combine(settings.Captcha.DiretorioDebug, $"{resultado.CodigoObjeto}_resultado.html"),
                html);
        }

        var rastro = RastroHtmlParser.Parse(html);

        resultado.TipoPostal = rastro.TipoPostal;
        resultado.PrevisaoEntrega = rastro.PrevisaoEntrega;
        resultado.Eventos = rastro.Eventos;

        return input;
    }
}
