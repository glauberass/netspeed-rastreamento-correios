using NetSpeed.Pipelines.Pipes;
using PipeliningLibrary;

namespace NetSpeed.Pipelines;

/// <summary>Registro dos pipelines do robô: a composição inteira, declarada em um lugar só.</summary>
public class PipelineController : PipelineGroup
{
    public PipelineController()
    {
        Pipeline("ConsultarObjeto").Pipe<ConsultarObjetoStartPipe>();
    }
}

/// <summary>Grupo de encerramento; separado do principal porque o que precisa rodar sempre não pode ser o que falha.</summary>
public class PipelineComum : PipelineGroup
{
    public PipelineComum()
    {
        Pipeline("FinalizarConsulta").Pipe<FinalizarPaginaPipe>();
    }
}
