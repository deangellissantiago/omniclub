namespace Checkin.Infrastructure.TotalPass;

public class TotalPassOptions
{
    /// <summary>Chave de integrador do OmniClub ("partner_api_key"), gerada no portal
    /// developers.totalpass.com. Vazia = integração TotalPass desligada.</summary>
    public string PartnerApiKey { get; set; } = string.Empty;

    /// <summary>Host do "Webhook - Checkin" (auth + cadastro de webhook). Produção:
    /// <c>https://gym-service-api.totalpass.com</c>; staging:
    /// <c>https://gym-service-api.staging.totalpass.com</c>. A chave do OmniClub e a unidade de
    /// teste do portal (DVP-HML-266) só autenticam em produção — confirmado em 2026-11-11.</summary>
    public string GymServiceBaseUrl { get; set; } = "https://gym-service-api.totalpass.com";

    /// <summary>Endereço público do OmniClub (ex.: <c>https://omniclub.run</c>) — base da URL de
    /// webhook cadastrada em cada unidade. Precisa ser acessível pela internet.</summary>
    public string WebhookBaseUrl { get; set; } = string.Empty;
}
