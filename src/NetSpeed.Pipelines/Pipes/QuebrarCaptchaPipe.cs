using NetSpeed.Captcha;
using NetSpeed.Domain.Modelos;
using NetSpeed.Domain.Portas;
using PipeliningLibrary;

namespace NetSpeed.Pipelines.Pipes;

/// <summary>Resolve o CAPTCHA (com retentativas) e submete a consulta do objeto.</summary>
public sealed class QuebrarCaptchaPipe : IPipe
{
    public object Run(dynamic input)
    {
        IPaginaRastreamento pagina = input.Pagina;
        ResolvedorDeCaptchaNoPortal resolvedor = input.Resolvedor;
        RastreamentoResultado resultado = input.Resultado;
        string codigo = input.Codigo;
        CancellationToken ct = input.CancellationToken;

        resolvedor.ResolverEConsultar(pagina, codigo, resultado, ct);

        return input;
    }
}
