using System.Dynamic;
using NetSpeed.Domain.Portas;
using PipeliningLibrary;

namespace NetSpeed.Pipelines.Pipes;

/// <summary>Fecha o navegador. Roda no <c>finally</c> do serviço, mesmo se o pipeline principal falhar.</summary>
public sealed class FinalizarPaginaPipe : IPipe
{
    public object Run(dynamic input)
    {
        if (input is ExpandoObject contexto &&
            ((IDictionary<string, object?>)contexto).TryGetValue("Pagina", out var valor) &&
            valor is IPaginaRastreamento pagina)
        {
            pagina.Dispose();
        }

        return input;
    }
}
