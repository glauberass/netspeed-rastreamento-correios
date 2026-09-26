using Microsoft.Extensions.Logging;
using NetSpeed.Domain.Configuracao;
using NetSpeed.Domain.Portas;

namespace NetSpeed.Infrastructure.Portal;

/// <summary>Cria uma sessão de navegador nova por consulta (isolamento de cookies e CAPTCHA).</summary>
public sealed class PaginaCorreiosFactory(AppSettings settings, ILoggerFactory loggerFactory)
    : IPaginaRastreamentoFactory
{
    public IPaginaRastreamento Criar() =>
        new PaginaCorreios(settings, loggerFactory.CreateLogger<PaginaCorreios>());
}
