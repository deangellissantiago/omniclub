using Checkin.Application.Exceptions;
using Checkin.Application.Ports.Integrations;
using Checkin.Application.UseCases.Billing;
using Checkin.Domain.Entities;
using Checkin.Domain.Enums;
using Checkin.Tests.Fakes;
using Moq;
using Xunit;

namespace Checkin.Tests;

/// <summary>
/// Cobre a assinatura mensal via Stripe (ver README, "Cobrança / Stripe"): cadastro autosserviço
/// nasce Inactive, só vira Active quando o webhook do Stripe confirma o pagamento.
/// </summary>
public class BillingServiceTests
{
    private const string TenantId = "tenant-1";
    private const string AdminId = "admin-1";
    private const string AdminEmail = "admin@escola.com";

    private readonly FakeTenantRepository _tenants = new();
    private readonly FakeAdminUserRepository _admins = new();
    private readonly Mock<IBillingGateway> _gateway = new();

    private BillingService BuildService() =>
        new(_tenants, _admins, _gateway.Object, new FakeCurrentTenantContext { TenantId = TenantId, AdminId = AdminId });

    private Tenant SeedTenant(SubscriptionStatus status = SubscriptionStatus.Inactive, string? stripeCustomerId = null)
    {
        var tenant = new Tenant { Id = TenantId, Name = "Escola Teste", SubscriptionStatus = status, StripeCustomerId = stripeCustomerId };
        _tenants.Tenants.Add(tenant);
        return tenant;
    }

    private void SeedAdmin() => _admins.Admins.Add(new AdminUser { Id = AdminId, TenantId = TenantId, Name = "Admin", Email = AdminEmail });

    [Fact]
    public async Task StartCheckoutAsync_creates_a_stripe_customer_when_the_tenant_has_none_yet()
    {
        SeedTenant();
        SeedAdmin();
        _gateway.SetupGet(g => g.IsConfigured).Returns(true);
        _gateway.Setup(g => g.EnsureCustomerAsync(TenantId, AdminEmail, "Escola Teste", It.IsAny<CancellationToken>()))
            .ReturnsAsync("cus_123");
        _gateway.Setup(g => g.CreateCheckoutSessionUrlAsync("cus_123", TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://checkout.stripe.com/session-abc");

        var service = BuildService();
        var dto = await service.StartCheckoutAsync();

        Assert.Equal("https://checkout.stripe.com/session-abc", dto.Url);
        Assert.Equal("cus_123", _tenants.Tenants.Single().StripeCustomerId);
        _gateway.Verify(g => g.EnsureCustomerAsync(TenantId, AdminEmail, "Escola Teste", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartCheckoutAsync_reuses_the_existing_stripe_customer()
    {
        SeedTenant(stripeCustomerId: "cus_existing");
        SeedAdmin();
        _gateway.SetupGet(g => g.IsConfigured).Returns(true);
        _gateway.Setup(g => g.CreateCheckoutSessionUrlAsync("cus_existing", TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://checkout.stripe.com/session-xyz");

        var service = BuildService();
        var dto = await service.StartCheckoutAsync();

        Assert.Equal("https://checkout.stripe.com/session-xyz", dto.Url);
        _gateway.Verify(g => g.EnsureCustomerAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartCheckoutAsync_throws_when_stripe_is_not_configured()
    {
        SeedTenant();
        SeedAdmin();
        _gateway.SetupGet(g => g.IsConfigured).Returns(false);

        var service = BuildService();

        await Assert.ThrowsAsync<ArgumentException>(() => service.StartCheckoutAsync());
    }

    [Fact]
    public async Task OpenPortalAsync_throws_when_there_is_no_subscription_yet()
    {
        SeedTenant();
        _gateway.SetupGet(g => g.IsConfigured).Returns(true);

        var service = BuildService();

        await Assert.ThrowsAsync<ArgumentException>(() => service.OpenPortalAsync());
    }

    [Fact]
    public async Task OpenPortalAsync_returns_the_portal_url_when_a_customer_already_exists()
    {
        SeedTenant(SubscriptionStatus.Active, "cus_123");
        _gateway.SetupGet(g => g.IsConfigured).Returns(true);
        _gateway.Setup(g => g.CreatePortalSessionUrlAsync("cus_123", It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://billing.stripe.com/portal-abc");

        var service = BuildService();
        var dto = await service.OpenPortalAsync();

        Assert.Equal("https://billing.stripe.com/portal-abc", dto.Url);
    }

    [Fact]
    public async Task GetStatusAsync_returns_the_tenant_current_subscription_status()
    {
        var periodEnd = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc);
        var tenant = SeedTenant(SubscriptionStatus.Active);
        tenant.SubscriptionCurrentPeriodEnd = periodEnd;

        var dto = await BuildService().GetStatusAsync();

        Assert.Equal(SubscriptionStatus.Active, dto.Status);
        Assert.Equal(periodEnd, dto.CurrentPeriodEnd);
    }

    [Fact]
    public async Task HandleWebhookAsync_activates_the_tenant_on_checkout_completed()
    {
        SeedTenant();
        _gateway.Setup(g => g.ParseWebhookEvent("payload", "sig")).Returns(
            new BillingWebhookEvent(BillingWebhookEventType.CheckoutCompleted, TenantId, "cus_123", "sub_123", SubscriptionStatus.Active, null));

        await BuildService().HandleWebhookAsync("payload", "sig");

        var tenant = _tenants.Tenants.Single();
        Assert.Equal(SubscriptionStatus.Active, tenant.SubscriptionStatus);
        Assert.Equal("cus_123", tenant.StripeCustomerId);
        Assert.Equal("sub_123", tenant.StripeSubscriptionId);
    }

    [Fact]
    public async Task HandleWebhookAsync_finds_the_tenant_by_stripe_customer_id_when_only_that_is_present()
    {
        var periodEnd = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        SeedTenant(SubscriptionStatus.Active, "cus_123");
        _gateway.Setup(g => g.ParseWebhookEvent("payload", "sig")).Returns(
            new BillingWebhookEvent(BillingWebhookEventType.SubscriptionUpdated, TenantId: null, "cus_123", "sub_123", SubscriptionStatus.PastDue, periodEnd));

        await BuildService().HandleWebhookAsync("payload", "sig");

        var tenant = _tenants.Tenants.Single();
        Assert.Equal(SubscriptionStatus.PastDue, tenant.SubscriptionStatus);
        Assert.Equal(periodEnd, tenant.SubscriptionCurrentPeriodEnd);
    }

    [Fact]
    public async Task HandleWebhookAsync_does_nothing_when_the_signature_is_invalid_or_event_is_irrelevant()
    {
        SeedTenant();
        _gateway.Setup(g => g.ParseWebhookEvent("payload", "sig-invalida")).Returns((BillingWebhookEvent?)null);

        await BuildService().HandleWebhookAsync("payload", "sig-invalida"); // não deve lançar

        Assert.Equal(SubscriptionStatus.Inactive, _tenants.Tenants.Single().SubscriptionStatus);
        Assert.Equal(0, _tenants.UpdateCallCount);
    }

    [Fact]
    public async Task HandleWebhookAsync_ignores_events_for_a_customer_we_do_not_recognize()
    {
        SeedTenant();
        _gateway.Setup(g => g.ParseWebhookEvent("payload", "sig")).Returns(
            new BillingWebhookEvent(BillingWebhookEventType.SubscriptionUpdated, TenantId: null, "cus_desconhecido", "sub_x", SubscriptionStatus.Active, null));

        await BuildService().HandleWebhookAsync("payload", "sig"); // não deve lançar

        Assert.Equal(0, _tenants.UpdateCallCount);
    }
}
