using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Checkin.Application.Ports.Integrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Checkin.Infrastructure.TotalPass;

/// <summary>
/// Adapter da API de reservas do TotalPass ("Booking - New", dev.totalpass.com). Autenticação
/// própria em booking-api (mesmo POST /partner/auth e mesmas chaves do check-in, mas outro host e
/// outro token — confirmado em 2026-11-11: os dois hosts devolvem tokens diferentes).
/// </summary>
public class TotalPassBookingGatewayAdapter : ITotalPassBookingGateway
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly TotalPassOptions _options;
    private readonly TotalPassTokenCache _tokens;
    private readonly ILogger<TotalPassBookingGatewayAdapter> _logger;

    public TotalPassBookingGatewayAdapter(HttpClient httpClient, IOptions<TotalPassOptions> options, TotalPassTokenCache tokens, ILogger<TotalPassBookingGatewayAdapter> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _tokens = tokens;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.PartnerApiKey);

    public async Task<IReadOnlyList<TotalPassPlan>> ListPlansAsync(string placeApiKey, CancellationToken ct = default)
    {
        using var response = await SendAsync(placeApiKey, HttpMethod.Get, "/partner/plans", null, ct);
        if (!response.IsSuccessStatusCode)
            throw new TotalPassException($"Não foi possível buscar os planos da unidade no TotalPass (HTTP {(int)response.StatusCode}).");

        // Real (2026-11-11): {"name","identifier","placeApiKey","Plans":[{"id","name","code",...}]}.
        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var list = root.ValueKind == JsonValueKind.Array ? root
            : root.TryGetProperty("Plans", out var p) || root.TryGetProperty("plans", out p) ? p : default;
        if (list.ValueKind != JsonValueKind.Array) return Array.Empty<TotalPassPlan>();

        return list.EnumerateArray()
            .Where(x => x.TryGetProperty("id", out _) && (!x.TryGetProperty("deletedAt", out var d) || d.ValueKind == JsonValueKind.Null))
            .Select(x => new TotalPassPlan(
                x.GetProperty("id").GetInt64(),
                x.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                x.TryGetProperty("code", out var c) ? c.GetString() : null))
            .ToList();
    }

    public async Task<TotalPassOccurrence?> CreateOccurrenceAsync(string placeApiKey, TotalPassOccurrenceRequest request, CancellationToken ct = default)
    {
        // Formatos da doc: eventDate "YYYY-MM-DD", startTime "hh:mm PM" (12 h), timezone "pt-BR".
        var body = new CreateOccurrenceBody(
            Title: request.Title,
            Responsible: request.Responsible,
            Description: string.IsNullOrWhiteSpace(request.Description) ? null : request.Description,
            Duration: request.DurationMinutes,
            Slots: request.Slots,
            PlanId: request.PlanId,
            Timezone: "pt-BR",
            EventDate: request.StartsAtLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            StartTime: request.StartsAtLocal.ToString("hh:mm tt", CultureInfo.InvariantCulture),
            ExternalReference: request.ExternalReference,
            Status: "ACTIVE");

        try
        {
            using var response = await SendAsync(placeApiKey, HttpMethod.Post, "/partner/event-occurrence", JsonContent.Create(body, options: Json), ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("TotalPass recusou a criação da aula ({Status}): {Body}", (int)response.StatusCode, Truncate(responseBody));
                return null;
            }

            using var doc = JsonDocument.Parse(responseBody);
            var eventId = ReadString(doc.RootElement, "eventId");
            var occurrenceUuid = ReadString(doc.RootElement, "eventOccurrenceUuid") ?? ReadString(doc.RootElement, "occurrenceUuid");
            if (eventId is null || occurrenceUuid is null)
            {
                _logger.LogWarning("Resposta do TotalPass sem eventId/eventOccurrenceUuid: {Body}", Truncate(responseBody));
                return null;
            }
            return new TotalPassOccurrence(eventId, occurrenceUuid);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or TotalPassException)
        {
            _logger.LogWarning(ex, "Falha ao publicar aula no TotalPass.");
            return null;
        }
    }

    public async Task SubscribeBookingWebhookAsync(string placeApiKey, string webhookPath, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(_options.WebhookBaseUrl, UriKind.Absolute, out var baseUri) || baseUri.Scheme != Uri.UriSchemeHttps)
            throw new TotalPassException("Endereço público do OmniClub não configurado (TotalPass:WebhookBaseUrl precisa ser https).");
        var url = new Uri(new Uri(baseUri.GetLeftPart(UriPartial.Authority) + "/"), webhookPath.TrimStart('/')).ToString();

        using var response = await SendAsync(placeApiKey, HttpMethod.Post, "/partner/webhook/subscribe",
            JsonContent.Create(new SubscribeBody(url)), ct);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("TotalPass recusou o webhook de reservas ({Status}): {Body}", (int)response.StatusCode, Truncate(responseBody));
            throw new TotalPassException($"O TotalPass recusou o cadastro do webhook de reservas (HTTP {(int)response.StatusCode}).");
        }
    }

    // ---- Helpers ----------------------------------------------------------------------------

    /// <summary>Envia com o token da unidade; se o TotalPass responder 401 (token vencido do lado
    /// deles antes do nosso cache), autentica de novo e tenta uma vez mais.</summary>
    private async Task<HttpResponseMessage> SendAsync(string placeApiKey, HttpMethod method, string path, HttpContent? content, CancellationToken ct)
    {
        if (!IsConfigured)
            throw new TotalPassException("Integração TotalPass não configurada no servidor (TotalPass:PartnerApiKey ausente).");

        var payload = content is null ? null : await content.ReadAsStringAsync(ct);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var token = _tokens.Get(CacheKey(placeApiKey)) ?? await AuthenticateAsync(placeApiKey, ct);
            var request = new HttpRequestMessage(method, Url(path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (payload is not null) request.Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request, ct);
            if (response.StatusCode != HttpStatusCode.Unauthorized || attempt == 1) return response;

            response.Dispose();
            _tokens.Invalidate(CacheKey(placeApiKey));
        }
        throw new InvalidOperationException("inalcançável");
    }

    private async Task<string> AuthenticateAsync(string placeApiKey, CancellationToken ct)
    {
        using var response = await _httpClient.PostAsJsonAsync(Url("/partner/auth"),
            new AuthRequest(placeApiKey, _options.PartnerApiKey), ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest)
            throw new TotalPassException("O TotalPass recusou as chaves da unidade na API de reservas.");
        if (!response.IsSuccessStatusCode)
            throw new TotalPassException($"O TotalPass não respondeu à autenticação de reservas (HTTP {(int)response.StatusCode}).");

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(Json, ct);
        if (string.IsNullOrWhiteSpace(auth?.Token))
            throw new TotalPassException("O TotalPass não devolveu o token de acesso da API de reservas.");

        _tokens.Set(CacheKey(placeApiKey), auth.Token);
        return auth.Token;
    }

    private static string CacheKey(string placeApiKey) => "booking|" + placeApiKey;

    private static string? ReadString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (element.TryGetProperty(name, out var value))
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null,
            };
        // Algumas respostas da doc embrulham o objeto (ex.: {"data": {...}}) — procura um nível abaixo.
        foreach (var child in element.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Object))
        {
            var nested = ReadString(child.Value, name);
            if (nested is not null) return nested;
        }
        return null;
    }

    private Uri Url(string path) => new(new Uri(_options.BookingBaseUrl.TrimEnd('/') + "/"), path.TrimStart('/'));

    private static string Truncate(string s) => s.Length > 300 ? s[..300] + "…" : s;

    private record AuthRequest(
        [property: JsonPropertyName("place_api_key")] string PlaceApiKey,
        [property: JsonPropertyName("partner_api_key")] string PartnerApiKey);

    private record AuthResponse(string? Token);

    private record SubscribeBody([property: JsonPropertyName("webhook_url")] string WebhookUrl);

    private record CreateOccurrenceBody(
        string Title, string Responsible,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Description,
        int Duration, int Slots, long PlanId, string Timezone, string EventDate, string StartTime,
        string ExternalReference, string Status);
}
