using Checkin.Application.Ports.Integrations;
using Checkin.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
// Aliases (não `using Stripe.Checkout;`/`using Stripe.BillingPortal;` direto): o namespace deste
// projeto (Checkin.Infrastructure.Stripe) colide com o namespace raiz do SDK (Stripe), e os dois
// sub-namespaces do SDK têm tipos de mesmo nome (Session/SessionService/SessionCreateOptions) —
// sem os aliases, toda referência qualificada "Stripe.X" dentro do código tentaria resolver
// primeiro contra o nosso próprio namespace (Checkin.Infrastructure.**Stripe**) e falhava.
using CheckoutApi = Stripe.Checkout;
using PortalApi = Stripe.BillingPortal;

namespace Checkin.Infrastructure.Stripe;

/// <summary>
/// Adapter de saída para IBillingGateway, usando o SDK oficial Stripe.net contra a Checkout
/// Session (assinatura mensal) e o Billing Portal. Guarda o <c>tenantId</c> como
/// <c>ClientReferenceId</c> da Checkout Session (volta em <c>checkout.session.completed</c>) e
/// como metadata do Customer, pra conseguirmos rastrear de volta pro tenant a partir de qualquer
/// evento de webhook.
/// </summary>
public class StripeBillingGateway : IBillingGateway
{
    private readonly BillingOptions _options;
    private readonly ILogger<StripeBillingGateway> _logger;
    private readonly StripeClient? _client;

    public StripeBillingGateway(IOptions<BillingOptions> options, ILogger<StripeBillingGateway> logger)
    {
        _options = options.Value;
        _logger = logger;
        _client = IsConfigured ? new StripeClient(_options.SecretKey) : null;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.SecretKey);

    public async Task<string?> EnsureCustomerAsync(string tenantId, string email, string name, CancellationToken ct = default)
    {
        if (_client is null) return null;

        try
        {
            var service = new CustomerService(_client);
            var customer = await service.CreateAsync(new CustomerCreateOptions
            {
                Email = email,
                Name = name,
                Metadata = new Dictionary<string, string> { ["tenantId"] = tenantId }
            }, cancellationToken: ct);
            return customer.Id;
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe: falha ao criar customer para o tenant {TenantId}", tenantId);
            return null;
        }
    }

    public async Task<string?> CreateCheckoutSessionUrlAsync(string customerId, string tenantId, CancellationToken ct = default)
    {
        if (_client is null) return null;

        try
        {
            var service = new CheckoutApi.SessionService(_client);
            var session = await service.CreateAsync(new CheckoutApi.SessionCreateOptions
            {
                Mode = "subscription",
                Customer = customerId,
                ClientReferenceId = tenantId,
                SuccessUrl = $"{_options.FrontendBaseUrl}/assinatura?status=sucesso",
                CancelUrl = $"{_options.FrontendBaseUrl}/assinatura?status=cancelado",
                LineItems = new List<CheckoutApi.SessionLineItemOptions>
                {
                    new()
                    {
                        Quantity = 1,
                        PriceData = new CheckoutApi.SessionLineItemPriceDataOptions
                        {
                            Currency = _options.Currency,
                            UnitAmount = _options.PriceAmountCents,
                            Recurring = new CheckoutApi.SessionLineItemPriceDataRecurringOptions { Interval = "month" },
                            ProductData = new CheckoutApi.SessionLineItemPriceDataProductDataOptions { Name = _options.ProductName }
                        }
                    }
                }
            }, cancellationToken: ct);
            return session.Url;
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe: falha ao criar checkout session para o tenant {TenantId}", tenantId);
            return null;
        }
    }

    public async Task<string?> CreatePortalSessionUrlAsync(string customerId, CancellationToken ct = default)
    {
        if (_client is null) return null;

        try
        {
            var service = new PortalApi.SessionService(_client);
            var session = await service.CreateAsync(new PortalApi.SessionCreateOptions
            {
                Customer = customerId,
                ReturnUrl = $"{_options.FrontendBaseUrl}/assinatura"
            }, cancellationToken: ct);
            return session.Url;
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe: falha ao criar portal session para o customer {CustomerId}", customerId);
            return null;
        }
    }

    public BillingWebhookEvent? ParseWebhookEvent(string payload, string signatureHeader)
    {
        if (string.IsNullOrWhiteSpace(_options.WebhookSecret))
        {
            _logger.LogWarning("Stripe webhook recebido mas Billing:WebhookSecret não está configurado — ignorado.");
            return null;
        }

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(payload, signatureHeader, _options.WebhookSecret);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe webhook recebido com assinatura inválida.");
            return null;
        }

        switch (stripeEvent.Type)
        {
            case "checkout.session.completed":
            {
                if (stripeEvent.Data.Object is not CheckoutApi.Session session) return null;
                return new BillingWebhookEvent(
                    BillingWebhookEventType.CheckoutCompleted,
                    TenantId: session.ClientReferenceId,
                    CustomerId: session.CustomerId,
                    SubscriptionId: session.SubscriptionId,
                    Status: SubscriptionStatus.Active,
                    CurrentPeriodEnd: null); // confirmado logo em seguida pelo customer.subscription.updated
            }
            case "customer.subscription.updated":
            case "customer.subscription.created":
            {
                if (stripeEvent.Data.Object is not Subscription subscription) return null;
                return new BillingWebhookEvent(
                    BillingWebhookEventType.SubscriptionUpdated,
                    TenantId: null,
                    CustomerId: subscription.CustomerId,
                    SubscriptionId: subscription.Id,
                    Status: MapStatus(subscription.Status),
                    CurrentPeriodEnd: subscription.Items?.Data?.FirstOrDefault()?.CurrentPeriodEnd);
            }
            case "customer.subscription.deleted":
            {
                if (stripeEvent.Data.Object is not Subscription subscription) return null;
                return new BillingWebhookEvent(
                    BillingWebhookEventType.SubscriptionDeleted,
                    TenantId: null,
                    CustomerId: subscription.CustomerId,
                    SubscriptionId: subscription.Id,
                    Status: SubscriptionStatus.Canceled,
                    CurrentPeriodEnd: null);
            }
            default:
                return null; // evento que não tratamos — 200 sem processar, sem gerar retry do Stripe
        }
    }

    private static SubscriptionStatus MapStatus(string stripeStatus) => stripeStatus switch
    {
        "active" or "trialing" => SubscriptionStatus.Active,
        "past_due" or "unpaid" => SubscriptionStatus.PastDue,
        "canceled" or "incomplete_expired" => SubscriptionStatus.Canceled,
        _ => SubscriptionStatus.Inactive
    };
}
