using Checkin.Domain.Enums;

namespace Checkin.Application.Ports.Integrations;

/// <summary>
/// Porta de saída (outbound) para o processador de pagamento (Stripe) — assinatura mensal única
/// por tenant (ver README, "Cobrança / Stripe"). Cadastro é autosserviço: o tenant nasce
/// <c>Inactive</c> e só vira <c>Active</c> quando o Stripe confirma o pagamento via webhook.
/// </summary>
public interface IBillingGateway
{
    /// <summary>Indica se há credenciais (chave secreta do Stripe) configuradas.</summary>
    bool IsConfigured { get; }

    /// <summary>Cria (ou retorna, se já existir) o Customer do Stripe para este tenant.</summary>
    Task<string?> EnsureCustomerAsync(string tenantId, string email, string name, CancellationToken ct = default);

    /// <summary>Cria uma Checkout Session (assinatura mensal, valor/moeda de <c>Billing:*</c>) e
    /// retorna a URL hospedada do Stripe pra redirecionar o admin. As URLs de sucesso/cancelamento
    /// são montadas aqui dentro a partir de <c>Billing:FrontendBaseUrl</c> — a Application não
    /// precisa saber a URL do frontend.</summary>
    Task<string?> CreateCheckoutSessionUrlAsync(string customerId, string tenantId, CancellationToken ct = default);

    /// <summary>Cria uma Billing Portal Session (gerenciar/cancelar assinatura, trocar cartão)
    /// e retorna a URL hospedada do Stripe.</summary>
    Task<string?> CreatePortalSessionUrlAsync(string customerId, CancellationToken ct = default);

    /// <summary>Valida a assinatura do webhook (header <c>Stripe-Signature</c>) e interpreta o
    /// evento. Retorna null se a assinatura for inválida ou o evento não for um dos que tratamos
    /// (nesses casos o controller só responde 200 sem processar, para não gerar retry).</summary>
    BillingWebhookEvent? ParseWebhookEvent(string payload, string signatureHeader);
}

public enum BillingWebhookEventType
{
    /// <summary>checkout.session.completed — primeira confirmação de pagamento.</summary>
    CheckoutCompleted,
    /// <summary>customer.subscription.updated — renovação, mudança de status (ex.: past_due).</summary>
    SubscriptionUpdated,
    /// <summary>customer.subscription.deleted — assinatura cancelada/expirada.</summary>
    SubscriptionDeleted
}

public record BillingWebhookEvent(
    BillingWebhookEventType Type,
    string? TenantId,
    string? CustomerId,
    string? SubscriptionId,
    SubscriptionStatus Status,
    DateTime? CurrentPeriodEnd);
