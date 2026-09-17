using System.Net.Http.Json;
using Checkin.Application.Ports.Integrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Checkin.Infrastructure.WhatsApp;

/// <summary>
/// Adapter de saída para IWhatsAppSender, implementado chamando o serviço standalone
/// `whatsapp-service` (POST /api/messages/template, header X-Api-Key) — não fala com a Meta
/// diretamente, quem faz isso é o outro serviço (ver seu README, "Sobre templates").
///
/// Falta uma coisa para funcionar de verdade: <see cref="WhatsAppOptions.BaseUrl"/> (e a conta
/// Meta configurada do lado do whatsapp-service). Enquanto isso, <see cref="IsConfigured"/> fica
/// falso e RetentionAlertService pula a régua de relacionamento sem tentar nada.
/// </summary>
public class WhatsAppServiceClient : IWhatsAppSender
{
    private readonly HttpClient _httpClient;
    private readonly WhatsAppOptions _options;
    private readonly ILogger<WhatsAppServiceClient> _logger;

    public WhatsAppServiceClient(HttpClient httpClient, IOptions<WhatsAppOptions> options, ILogger<WhatsAppServiceClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        if (Uri.TryCreate(EnsureTrailingSlash(_options.BaseUrl), UriKind.Absolute, out var baseUri))
        {
            _httpClient.BaseAddress = baseUri;
        }
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.BaseUrl);

    public async Task<WhatsAppSendResult> SendTemplateAsync(
        string toPhone, string templateName, string languageCode, IReadOnlyList<string> parameters, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return new WhatsAppSendResult(false, null, "WhatsApp:BaseUrl não configurado (whatsapp-service não apontado).");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "messages/template")
        {
            Content = JsonContent.Create(new
            {
                to = toPhone,
                templateName,
                languageCode,
                parameters,
            }),
        };
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            request.Headers.Add("X-Api-Key", _options.ApiKey);
        }

        try
        {
            using var response = await _httpClient.SendAsync(request, ct);
            var body = await response.Content.ReadFromJsonAsync<WhatsAppServiceResponse>(cancellationToken: ct);

            if (response.IsSuccessStatusCode && body?.Status == "sent")
            {
                return new WhatsAppSendResult(true, body.ProviderMessageId, null);
            }

            var error = body?.Message ?? $"whatsapp-service retornou {(int)response.StatusCode} ({body?.Status ?? "sem corpo"}).";
            _logger.LogWarning("Falha ao enviar WhatsApp (template {TemplateName}): {Error}", templateName, error);
            return new WhatsAppSendResult(false, null, error);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Falha de rede chamando o whatsapp-service (template {TemplateName}).", templateName);
            return new WhatsAppSendResult(false, null, $"Falha de rede chamando o whatsapp-service: {ex.Message}");
        }
    }

    private static string EnsureTrailingSlash(string url) =>
        string.IsNullOrEmpty(url) || url.EndsWith('/') ? url : url + "/";

    private record WhatsAppServiceResponse(string Status, string? ProviderMessageId, string? Message);
}
