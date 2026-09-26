namespace NetSpeed.Domain.Modelos;

/// <summary>Classificação dos erros possíveis; serializado como texto no JSON.</summary>
public enum CodigoErro
{
    /// <summary>O código informado não tem o formato AA123456789BR.</summary>
    CodigoInvalido,

    /// <summary>O portal respondeu, mas não há objeto com esse código.</summary>
    ObjetoNaoEncontrado,

    /// <summary>Esgotou o número de tentativas sem o portal aceitar o CAPTCHA.</summary>
    CaptchaNaoResolvido,

    /// <summary>Portal fora do ar, página não carregou ou o navegador não abriu.</summary>
    PortalIndisponivel,

    /// <summary>Uma etapa excedeu o tempo configurado.</summary>
    Timeout,

    /// <summary>A página não tem os elementos esperados (layout do portal mudou).</summary>
    LayoutAlterado,

    /// <summary>Falha não classificada.</summary>
    ErroInesperado
}
