using System.Text;
using Checkin.Application.Ports.Integrations;

namespace Checkin.Api.Integrations;

/// <summary>
/// Adapter HTTP do webhook de saída (ver OutboundWebhookService). Timeout curto (10s) de
/// propósito: o destino deve responder 2xx assim que receber e processar depois — se demorar
/// mais que isso, conta como falha e entra no reenvio.
/// </summary>
public class HttpOutboundWebhookSender : IOutboundWebhookSender
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _httpClient;

    public HttpOutboundWebhookSender(HttpClient httpClient) => _httpClient = httpClient;

    public async Task<OutboundWebhookSendResult> SendAsync(OutboundWebhookRequest request, CancellationToken ct = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, request.Url)
        {
            Content = new StringContent(request.Body, Encoding.UTF8, "application/json"),
        };
        message.Headers.Add("X-Omniclub-Signature", request.Signature);
        message.Headers.Add("X-Omniclub-Event", request.EventType);
        message.Headers.Add("X-Omniclub-Delivery", request.DeliveryId);
        message.Headers.UserAgent.ParseAdd("OmniClub-Webhooks/1.0");

        try
        {
            using var response = await _httpClient.SendAsync(message, ct);
            var status = (int)response.StatusCode;
            if (response.IsSuccessStatusCode) return new OutboundWebhookSendResult(true, status, null);

            var body = await response.Content.ReadAsStringAsync(ct);
            var snippet = body.Length > 300 ? body[..300] + "…" : body;
            return new OutboundWebhookSendResult(false, status, $"HTTP {status}{(snippet.Length > 0 ? $": {snippet}" : "")}");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new OutboundWebhookSendResult(false, null, $"Sem resposta em {Timeout.TotalSeconds:0}s (timeout).");
        }
        catch (HttpRequestException ex)
        {
            return new OutboundWebhookSendResult(false, null, $"Falha de conexão: {ex.Message}");
        }
    }
}
