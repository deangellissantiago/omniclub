namespace Checkin.Infrastructure.Stripe;

public class BillingOptions
{
    /// <summary>Chave secreta do Stripe (sk_test_... em Sandbox, sk_live_... em produção).</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Secret do endpoint de webhook (whsec_...), pra validar o header Stripe-Signature.</summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>Preço da assinatura mensal, em centavos (ex.: 9900 = R$ 99,00). Valor de
    /// exemplo — ainda não decidido pelo cliente, ajuste aqui quando definir.</summary>
    public long PriceAmountCents { get; set; } = 9900;

    /// <summary>Moeda ISO 4217 minúscula (o Stripe exige minúsculo), ex.: "brl".</summary>
    public string Currency { get; set; } = "brl";

    public string ProductName { get; set; } = "OmniClub — Assinatura mensal";

    /// <summary>Base da URL do frontend, pra montar as URLs de sucesso/cancelamento/retorno do
    /// Stripe (ex.: https://app.omniclub.com.br). Mesmo valor de FRONTEND_URL.</summary>
    public string FrontendBaseUrl { get; set; } = "http://localhost:5271";
}
