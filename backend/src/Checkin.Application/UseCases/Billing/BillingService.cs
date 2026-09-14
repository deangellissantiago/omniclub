using Checkin.Application.DTOs.Billing;
using Checkin.Application.Exceptions;
using Checkin.Application.Ports;
using Checkin.Application.Ports.Integrations;
using Checkin.Application.Ports.Repositories;
using Checkin.Domain.Enums;

namespace Checkin.Application.UseCases.Billing;

/// <summary>
/// Assinatura mensal por tenant via Stripe (ver README, "Cobrança / Stripe"). Cadastro é
/// autosserviço (AuthService.RegisterAsync): o tenant nasce <see cref="SubscriptionStatus.Inactive"/>
/// e só vira <see cref="SubscriptionStatus.Active"/> quando o Stripe confirma o pagamento via
/// webhook (<see cref="HandleWebhookAsync"/>). Enquanto não está Active,
/// <c>SubscriptionGateMiddleware</c> bloqueia todas as rotas autenticadas exceto login e as
/// deste serviço.
/// </summary>
public class BillingService
{
    private readonly ITenantRepository _tenants;
    private readonly IAdminUserRepository _admins;
    private readonly IBillingGateway _gateway;
    private readonly ICurrentTenantContext _tenantContext;

    public BillingService(ITenantRepository tenants, IAdminUserRepository admins, IBillingGateway gateway, ICurrentTenantContext tenantContext)
    {
        _tenants = tenants;
        _admins = admins;
        _gateway = gateway;
        _tenantContext = tenantContext;
    }

    public async Task<SubscriptionStatusDto> GetStatusAsync(CancellationToken ct = default)
    {
        var tenant = await GetCurrentTenantAsync(ct);
        return new SubscriptionStatusDto(tenant.SubscriptionStatus, tenant.SubscriptionCurrentPeriodEnd);
    }

    public async Task<CheckoutSessionDto> StartCheckoutAsync(CancellationToken ct = default)
    {
        if (!_gateway.IsConfigured)
        {
            throw new ArgumentException("Cobrança (Stripe) não configurada — defina Billing:SecretKey.");
        }

        var tenant = await GetCurrentTenantAsync(ct);

        if (tenant.StripeCustomerId is null)
        {
            var admin = await _admins.GetByIdAsync(_tenantContext.AdminId, ct)
                ?? throw new NotFoundException("Administrador não encontrado.");

            var customerId = await _gateway.EnsureCustomerAsync(tenant.Id, admin.Email, tenant.Name, ct)
                ?? throw new ArgumentException("Não foi possível criar o cliente no Stripe agora (ver logs do backend).");

            tenant.StripeCustomerId = customerId;
            await _tenants.UpdateAsync(tenant, ct);
        }

        var url = await _gateway.CreateCheckoutSessionUrlAsync(tenant.StripeCustomerId!, tenant.Id, ct)
            ?? throw new ArgumentException("Não foi possível iniciar o checkout no Stripe agora (ver logs do backend).");

        return new CheckoutSessionDto(url);
    }

    public async Task<BillingPortalSessionDto> OpenPortalAsync(CancellationToken ct = default)
    {
        if (!_gateway.IsConfigured)
        {
            throw new ArgumentException("Cobrança (Stripe) não configurada — defina Billing:SecretKey.");
        }

        var tenant = await GetCurrentTenantAsync(ct);
        if (tenant.StripeCustomerId is null)
        {
            throw new ArgumentException("Ainda não existe assinatura pra gerenciar — inicie o checkout primeiro.");
        }

        var url = await _gateway.CreatePortalSessionUrlAsync(tenant.StripeCustomerId, ct)
            ?? throw new ArgumentException("Não foi possível abrir o portal de cobrança do Stripe agora (ver logs do backend).");

        return new BillingPortalSessionDto(url);
    }

    /// <summary>Chamado pelo BillingWebhookController — sem contexto de tenant autenticado (o
    /// Stripe não manda JWT), por isso não usa ICurrentTenantContext.</summary>
    public async Task HandleWebhookAsync(string payload, string signatureHeader, CancellationToken ct = default)
    {
        var evt = _gateway.ParseWebhookEvent(payload, signatureHeader);
        if (evt is null) return; // assinatura inválida ou evento que não tratamos — 200 sem processar

        var tenant = evt.TenantId is not null
            ? await _tenants.GetByIdAsync(evt.TenantId, ct)
            : evt.CustomerId is not null
                ? await _tenants.GetByStripeCustomerIdAsync(evt.CustomerId, ct)
                : null;
        if (tenant is null) return; // evento de um customer que não é (mais) nosso — ignora

        tenant.SubscriptionStatus = evt.Status;
        tenant.SubscriptionCurrentPeriodEnd = evt.CurrentPeriodEnd;
        if (evt.CustomerId is not null) tenant.StripeCustomerId = evt.CustomerId;
        if (evt.SubscriptionId is not null) tenant.StripeSubscriptionId = evt.SubscriptionId;

        await _tenants.UpdateAsync(tenant, ct);
    }

    private async Task<Domain.Entities.Tenant> GetCurrentTenantAsync(CancellationToken ct) =>
        await _tenants.GetByIdAsync(_tenantContext.TenantId, ct)
            ?? throw new NotFoundException("Tenant não encontrado.");
}
