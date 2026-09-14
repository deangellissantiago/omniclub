using System.Text.Json;
using Checkin.Application.Ports.Repositories;
using Checkin.Domain.Enums;

namespace Checkin.Api.Middleware;

/// <summary>
/// Bloqueia todas as rotas autenticadas de um tenant sem assinatura ativa (ver README,
/// "Cobrança / Stripe") — exceto login/cadastro e a própria tela de cobrança, senão ninguém
/// conseguiria assinar pra sair do bloqueio. Responde 402 Payment Required (não 403): é
/// especificamente "falta pagar", não "sem permissão".
///
/// Rodas sem usuário autenticado (ex.: o webhook do Wellhub, que não manda JWT) passam direto —
/// esse gate só olha pra requisições de admin logado.
/// </summary>
public class SubscriptionGateMiddleware
{
    private static readonly string[] ExemptPathPrefixes = { "/api/auth", "/api/billing", "/swagger" };

    private readonly RequestDelegate _next;

    public SubscriptionGateMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ITenantRepository tenantRepository)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var isExempt = ExemptPathPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        if (!isExempt && context.User.Identity?.IsAuthenticated == true)
        {
            var tenantId = context.User.FindFirst("tenantId")?.Value;
            var tenant = tenantId is null ? null : await tenantRepository.GetByIdAsync(tenantId, context.RequestAborted);

            if (tenant is null || tenant.SubscriptionStatus != SubscriptionStatus.Active)
            {
                context.Response.ContentType = "application/json";
                context.Response.StatusCode = StatusCodes.Status402PaymentRequired;
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    error = "Assinatura inativa — complete o pagamento para continuar usando o sistema.",
                    subscriptionStatus = (tenant?.SubscriptionStatus ?? SubscriptionStatus.Inactive).ToString()
                }));
                return;
            }
        }

        await _next(context);
    }
}
