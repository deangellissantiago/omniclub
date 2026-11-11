namespace Checkin.Application.Ports.Integrations;

/// <summary>
/// Porta de saída (outbound) para o TotalPass — "Webhook - Checkin" da documentação
/// (dev.totalpass.com). Contrato confirmado em 2026-11-11 contra a API de produção com a chave de
/// integrador do OmniClub e a unidade de teste do portal (DVP-HML-266):
/// <c>POST /partner/auth</c> com <c>{place_api_key, partner_api_key}</c> → JWT de 24 h, usado como
/// Bearer em <c>/partner/webhook/*</c>. A confirmação do check-in é um
/// <c>POST</c> na URL que vem no próprio evento (<c>endpoint</c>).
/// </summary>
public interface ITotalPassGateway
{
    /// <summary>Há chave de integrador (<c>TotalPass:PartnerApiKey</c>) configurada.</summary>
    bool IsConfigured { get; }

    /// <summary>Autentica a unidade e cadastra (ou atualiza) nela o webhook de check-in apontando
    /// para <paramref name="webhookPath"/> no endereço público do OmniClub
    /// (<c>TotalPass:WebhookBaseUrl</c>). Lança <see cref="TotalPassException"/> com mensagem
    /// pronta para o painel se as chaves forem recusadas ou o TotalPass falhar.</summary>
    Task<TotalPassConnection> ConnectPlaceAsync(string placeApiKey, string webhookPath, CancellationToken ct = default);

    /// <summary>Confirma o check-in na URL de confirmação recebida no evento. Resultado negativo
    /// (expirado, indisponível, não encontrado) volta como <see cref="TotalPassConfirmation"/>;
    /// falha de rede/5xx lança exceção, pra o evento ser reprocessado.</summary>
    Task<TotalPassConfirmation> ConfirmCheckinAsync(string placeApiKey, string confirmationUrl, CancellationToken ct = default);
}

/// <param name="PlaceId">Id numérico da unidade no TotalPass (vem dentro dos planos da resposta
/// do /partner/auth); nulo se a resposta não trouxer.</param>
/// <param name="WebhookUrl">URL completa que ficou cadastrada no TotalPass.</param>
public record TotalPassConnection(string? PlaceName, string? PlaceId, string WebhookUrl);

public record TotalPassConfirmation(bool Confirmed, string? RejectionReason)
{
    public static readonly TotalPassConfirmation Ok = new(true, null);
    public static TotalPassConfirmation Rejected(string reason) => new(false, reason);
}

/// <summary>Erro do TotalPass com mensagem já em português, pronta para mostrar no painel.</summary>
public class TotalPassException : Exception
{
    public TotalPassException(string message) : base(message) { }
}
