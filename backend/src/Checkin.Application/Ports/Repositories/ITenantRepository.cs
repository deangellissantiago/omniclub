using Checkin.Domain.Entities;

namespace Checkin.Application.Ports.Repositories;

public interface ITenantRepository
{
    Task<Tenant?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<Tenant> CreateAsync(Tenant tenant, CancellationToken ct = default);
    Task<bool> AnyAsync(CancellationToken ct = default);
    Task<bool> UpdateAsync(Tenant tenant, CancellationToken ct = default);

    /// <summary>Usado pelo webhook do Stripe: eventos de assinatura (customer.subscription.*)
    /// só trazem o customer id, não o tenantId — precisamos achar o tenant dono.</summary>
    Task<Tenant?> GetByStripeCustomerIdAsync(string stripeCustomerId, CancellationToken ct = default);
}
