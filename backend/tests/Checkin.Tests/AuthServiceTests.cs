using Checkin.Application.DTOs.Auth;
using Checkin.Application.Exceptions;
using Checkin.Application.Ports.Security;
using Checkin.Application.UseCases.Auth;
using Checkin.Domain.Enums;
using Checkin.Tests.Fakes;
using Moq;
using Xunit;

namespace Checkin.Tests;

/// <summary>Cobre o cadastro autosserviço (RegisterAsync) — o tenant precisa nascer Inactive,
/// senão o cliente teria acesso de graça sem passar pelo checkout do Stripe (ver
/// BillingServiceTests / SubscriptionGateMiddleware).</summary>
public class AuthServiceTests
{
    private readonly FakeAdminUserRepository _admins = new();
    private readonly FakeTenantRepository _tenants = new();
    private readonly FakePasswordHasher _hasher = new();
    private readonly Mock<IJwtTokenService> _jwt = new();

    private AuthService BuildService()
    {
        _jwt.Setup(j => j.GenerateToken(It.IsAny<Domain.Entities.AdminUser>())).Returns("fake-jwt-token");
        return new AuthService(_admins, _tenants, _hasher, _jwt.Object);
    }

    [Fact]
    public async Task RegisterAsync_creates_an_inactive_tenant_and_its_first_admin()
    {
        var service = BuildService();

        var result = await service.RegisterAsync(new RegisterRequest("Escola Nova", "Maria", "maria@escola.com", "Senha@123"));

        Assert.Equal("fake-jwt-token", result.Token);
        Assert.Equal("Maria", result.AdminName);
        Assert.Equal("Escola Nova", result.TenantName);

        var tenant = Assert.Single(_tenants.Tenants);
        Assert.Equal(SubscriptionStatus.Inactive, tenant.SubscriptionStatus); // não pode nascer Active

        var admin = Assert.Single(_admins.Admins);
        Assert.Equal("maria@escola.com", admin.Email);
        Assert.Equal(tenant.Id, admin.TenantId);
        Assert.True(_hasher.Verify("Senha@123", admin.PasswordHash));
    }

    [Fact]
    public async Task RegisterAsync_throws_conflict_when_the_email_is_already_taken()
    {
        _admins.Admins.Add(new Domain.Entities.AdminUser { Email = "maria@escola.com" });
        var service = BuildService();

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.RegisterAsync(new RegisterRequest("Escola Nova", "Maria", "maria@escola.com", "Senha@123")));
    }
}
