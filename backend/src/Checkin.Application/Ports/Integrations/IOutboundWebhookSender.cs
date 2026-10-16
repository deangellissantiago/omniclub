namespace Checkin.Application.Ports.Integrations;

/// <summary>
/// Porta de saída (outbound) para entregar um webhook do OmniClub ao sistema do cliente. Só faz
/// o POST HTTP com os headers já calculados — assinatura, reenvio e log ficam em
/// OutboundWebhookService.
/// </summary>
public interface IOutboundWebhookSender
{
    Task<OutboundWebhookSendResult> SendAsync(OutboundWebhookRequest request, CancellationToken ct = default);
}

public record OutboundWebhookRequest(string Url, string Body, string Signature, string EventType, string DeliveryId);

/// <summary><paramref name="StatusCode"/> nulo = nem chegou a ter resposta HTTP (timeout, DNS,
/// conexão recusada) — o motivo vai em <paramref name="Error"/>.</summary>
public record OutboundWebhookSendResult(bool Success, int? StatusCode, string? Error);
