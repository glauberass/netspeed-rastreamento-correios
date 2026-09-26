using NetSpeed.Domain.Portas;
using PipeliningLibrary;

namespace NetSpeed.Pipelines.Pipes;

/// <summary>Abre o navegador e carrega o portal. A página fica no contexto para o encerramento liberar.</summary>
public sealed class AbrirPaginaPipe : IPipe
{
    public object Run(dynamic input)
    {
        IPaginaRastreamentoFactory fabrica = input.PaginaFactory;

        IPaginaRastreamento pagina = fabrica.Criar();

        // Guardada antes de Abrir: se Abrir falhar no meio, o encerramento ainda encontra o
        // navegador para fechar.
        input.Pagina = pagina;
        pagina.Abrir();

        return input;
    }
}
