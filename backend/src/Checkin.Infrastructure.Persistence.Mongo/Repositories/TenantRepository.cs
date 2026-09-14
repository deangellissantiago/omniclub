using Checkin.Application.Ports.Repositories;
using Checkin.Domain.Entities;
using MongoDB.Driver;

namespace Checkin.Infrastructure.Persistence.Mongo.Repositories;

public class TenantRepository : ITenantRepository
{
    private readonly MongoContext _context;

    public TenantRepository(MongoContext context) => _context = context;

    public async Task<Tenant?> GetByIdAsync(string id, CancellationToken ct = default) =>
        await _context.Tenants.Find(t => t.Id == id).FirstOrDefaultAsync(ct);

    public async Task<Tenant> CreateAsync(Tenant tenant, CancellationToken ct = default)
    {
        await _context.Tenants.InsertOneAsync(tenant, cancellationToken: ct);
        return tenant;
    }

    public async Task<bool> AnyAsync(CancellationToken ct = default) =>
        await _context.Tenants.Find(FilterDefinition<Tenant>.Empty).AnyAsync(ct);

    public async Task<bool> UpdateAsync(Tenant tenant, CancellationToken ct = default)
    {
        var result = await _context.Tenants.ReplaceOneAsync(t => t.Id == tenant.Id, tenant, cancellationToken: ct);
        return result.ModifiedCount > 0;
    }

    public async Task<Tenant?> GetByStripeCustomerIdAsync(string stripeCustomerId, CancellationToken ct = default) =>
        await _context.Tenants.Find(t => t.StripeCustomerId == stripeCustomerId).FirstOrDefaultAsync(ct);
}
