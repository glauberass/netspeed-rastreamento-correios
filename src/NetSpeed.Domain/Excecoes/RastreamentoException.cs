using NetSpeed.Domain.Modelos;

namespace NetSpeed.Domain.Excecoes;

/// <summary>Falha de negócio ou de infraestrutura já classificada por <see cref="CodigoErro"/>.</summary>
public sealed class RastreamentoException : Exception
{
    public RastreamentoException(CodigoErro codigo, string mensagem, Exception? inner = null)
        : base(mensagem, inner)
    {
        Codigo = codigo;
    }

    public CodigoErro Codigo { get; }

    public ErroRastreamento ParaErro() => new(Codigo, Message);
}
