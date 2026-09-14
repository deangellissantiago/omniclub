using Checkin.Application.Ports.Repositories;
using Checkin.Application.Ports.Security;
using Checkin.Domain.Entities;

namespace Checkin.Tests.Fakes;

public class FakeTenantRepository : ITenantRepository
{
    public List<Tenant> Tenants { get; } = new();
    public int UpdateCallCount { get; private set; }

    public Task<Tenant?> GetByIdAsync(string id, CancellationToken ct = default) =>
        Task.FromResult(Tenants.FirstOrDefault(t => t.Id == id));

    public Task<Tenant> CreateAsync(Tenant tenant, CancellationToken ct = default)
    {
        Tenants.Add(tenant);
        return Task.FromResult(tenant);
    }

    public Task<bool> AnyAsync(CancellationToken ct = default) => Task.FromResult(Tenants.Count > 0);

    public Task<bool> UpdateAsync(Tenant tenant, CancellationToken ct = default)
    {
        UpdateCallCount++;
        return Task.FromResult(true);
    }

    public Task<Tenant?> GetByStripeCustomerIdAsync(string stripeCustomerId, CancellationToken ct = default) =>
        Task.FromResult(Tenants.FirstOrDefault(t => t.StripeCustomerId == stripeCustomerId));
}

public class FakeAdminUserRepository : IAdminUserRepository
{
    public List<AdminUser> Admins { get; } = new();

    public Task<AdminUser?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        Task.FromResult(Admins.FirstOrDefault(a => a.Email == email));

    public Task<AdminUser?> GetByIdAsync(string id, CancellationToken ct = default) =>
        Task.FromResult(Admins.FirstOrDefault(a => a.Id == id));

    public Task<AdminUser> CreateAsync(AdminUser admin, CancellationToken ct = default)
    {
        Admins.Add(admin);
        return Task.FromResult(admin);
    }
}

public class FakePasswordHasher : IPasswordHasher
{
    public string Hash(string password) => $"hashed:{password}";
    public bool Verify(string password, string hash) => hash == $"hashed:{password}";
}
