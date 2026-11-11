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
/// Adapter HTTP do TotalPass ("Webhook - Checkin", dev.totalpass.com). Autenticação, cadastro do
/// webhook e o formato da resposta do /partner/auth foram confirmados ao vivo em 2026-11-11 com a
/// unidade de teste do portal. A confirmação do check-in segue a documentação (POST na URL do
/// campo <c>endpoint</c> do evento; 200 = confirmado, 422 = expirado/indisponível, 404 = não
/// encontrado) — o header de autenticação dela não é documentado, então mandamos o mesmo Bearer
/// da unidade (inofensivo se não for exigido).
/// </summary>
public class TotalPassGatewayAdapter : ITotalPassGateway
{
    private const string CheckinWebhookType = "CHECKIN";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly TotalPassOptions _options;
    private readonly TotalPassTokenCache _tokens;
    private readonly ILogger<TotalPassGatewayAdapter> _logger;

    public TotalPassGatewayAdapter(HttpClient httpClient, IOptions<TotalPassOptions> options, TotalPassTokenCache tokens, ILogger<TotalPassGatewayAdapter> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _tokens = tokens;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.PartnerApiKey);

    public async Task<TotalPassConnection> ConnectPlaceAsync(string placeApiKey, string webhookPath, CancellationToken ct = default)
    {
        EnsureConfigured();
        if (!Uri.TryCreate(_options.WebhookBaseUrl, UriKind.Absolute, out var baseUri) || baseUri.Scheme != Uri.UriSchemeHttps)
            throw new TotalPassException("Endereço público do OmniClub não configurado (TotalPass:WebhookBaseUrl precisa ser https).");
        var webhookUrl = new Uri(new Uri(baseUri.GetLeftPart(UriPartial.Authority) + "/"), webhookPath.TrimStart('/')).ToString();

        // Conexão sempre autentica de novo: é também o teste de que a chave da unidade é válida.
        _tokens.Invalidate(placeApiKey);
        var (token, auth) = await AuthenticateAsync(placeApiKey, ct);

        var existing = await GetWebhooksAsync(token, ct);
        var alreadyHasCheckin = existing.Any(type => string.Equals(type, CheckinWebhookType, StringComparison.OrdinalIgnoreCase));

        // Já tem webhook de check-in → atualiza a URL (PUT); senão cria (POST). Assim reconectar
        // nunca deixa a unidade com dois webhooks apontando pra lugares diferentes.
        using var request = new HttpRequestMessage(alreadyHasCheckin ? HttpMethod.Put : HttpMethod.Post,
            Url(alreadyHasCheckin ? "/partner/webhook/update" : "/partner/webhook/create"))
        {
            Content = JsonContent.Create(new WebhookRegistration(webhookUrl, CheckinWebhookType)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("TotalPass recusou o cadastro do webhook ({Status}): {Body}", (int)response.StatusCode, body);
            throw new TotalPassException($"O TotalPass recusou o cadastro do webhook (HTTP {(int)response.StatusCode}). Tente de novo em instantes.");
        }

        var placeId = auth.Place?.Plans?.FirstOrDefault(p => p.PlaceId is not null)?.PlaceId?.ToString();
        return new TotalPassConnection(auth.Place?.Name, placeId, webhookUrl);
    }

    public async Task<TotalPassConfirmation> ConfirmCheckinAsync(string placeApiKey, string confirmationUrl, CancellationToken ct = default)
    {
        EnsureConfigured();
        Uri url;
        try
        {
            url = ValidateConfirmationUrl(confirmationUrl);
        }
        catch (TotalPassException ex)
        {
            _logger.LogWarning("Check-in TotalPass com URL de confirmação inválida: {Url}", confirmationUrl);
            return TotalPassConfirmation.Rejected(ex.Message);
        }

        var token = _tokens.Get(placeApiKey) ?? (await AuthenticateAsync(placeApiKey, ct)).Token;
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        // Só a resposta documentada aprova: 200 com corpo "1". Em 2026-11-11, um POST num host
        // admin.totalpass.com devolveu 303 → tela de login (AWS SSO); seguindo o redirect isso
        // virava um 200 de HTML. Daí as duas travas: sem auto-redirect (ver
        // ServiceCollectionExtensions) e checagem do corpo.
        if ((int)response.StatusCode is >= 300 and < 400)
        {
            _logger.LogWarning("TotalPass redirecionou a confirmação ({Status}) para {Location}", (int)response.StatusCode, response.Headers.Location);
            return TotalPassConfirmation.Rejected($"O TotalPass não aceitou a confirmação (redirecionou, HTTP {(int)response.StatusCode}).");
        }

        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
            case HttpStatusCode.Created:
                if (IsConfirmationBody(body)) return TotalPassConfirmation.Ok;
                _logger.LogWarning("Resposta inesperada do TotalPass na confirmação (HTTP {Status}): {Body}", (int)response.StatusCode, Truncate(body));
                return TotalPassConfirmation.Rejected("Resposta inesperada do TotalPass na confirmação — check-in não aprovado.");

            case HttpStatusCode.UnprocessableEntity:
                return TotalPassConfirmation.Rejected(ReadValidationMessage(body) ?? "Check-in recusado pelo TotalPass.");

            case HttpStatusCode.NotFound:
                return TotalPassConfirmation.Rejected("Check-in não encontrado no TotalPass.");

            case HttpStatusCode.Unauthorized:
            case HttpStatusCode.Forbidden:
                // Token pode ter vencido do lado deles antes do nosso cache — pede outro na próxima.
                _tokens.Invalidate(placeApiKey);
                throw new HttpRequestException($"TotalPass recusou a autenticação na confirmação (HTTP {(int)response.StatusCode}).");

            default:
                throw new HttpRequestException($"TotalPass respondeu HTTP {(int)response.StatusCode} na confirmação: {Truncate(body)}");
        }
    }

    // ---- Helpers ----------------------------------------------------------------------------

    private async Task<(string Token, AuthResponse Auth)> AuthenticateAsync(string placeApiKey, CancellationToken ct)
    {
        using var response = await _httpClient.PostAsJsonAsync(Url("/partner/auth"),
            new AuthRequest(placeApiKey, _options.PartnerApiKey), ct);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest)
            throw new TotalPassException("O TotalPass recusou as chaves. Confira a chave da unidade (place_api_key) gerada no painel do TotalPass.");
        if (!response.IsSuccessStatusCode)
            throw new TotalPassException($"O TotalPass não respondeu à autenticação (HTTP {(int)response.StatusCode}). Tente de novo em instantes.");

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(Json, ct)
            ?? throw new TotalPassException("Resposta vazia do TotalPass na autenticação.");
        if (string.IsNullOrWhiteSpace(auth.Token))
            throw new TotalPassException("O TotalPass não devolveu o token de acesso.");

        _tokens.Set(placeApiKey, auth.Token);
        return (auth.Token, auth);
    }

    /// <summary>Tipos dos webhooks já cadastrados na unidade. A documentação mostra
    /// <c>{"webhooks":[{"webhook_url","webhook_type"}]}</c>, mas a API real (2026-11-11) devolve uma
    /// lista pura com <c>{"id","url","webhookType",...}</c> — aceita os dois formatos.</summary>
    private async Task<IReadOnlyList<string?>> GetWebhooksAsync(string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Url("/partner/webhook/get"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return Array.Empty<string?>();

        var body = await response.Content.ReadAsStringAsync(ct);
        try
        {
            using var doc = JsonDocument.Parse(body);
            var list = doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement
                : doc.RootElement.TryGetProperty("webhooks", out var w) ? w : default;
            if (list.ValueKind != JsonValueKind.Array) return Array.Empty<string?>();

            return list.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object)
                .Select(item => item.TryGetProperty("webhookType", out var t) || item.TryGetProperty("webhook_type", out t) ? t.GetString() : null)
                .ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<string?>();
        }
    }

    /// <summary>
    /// A URL de confirmação vem dentro do evento, e o evento não tem assinatura — sem esta
    /// checagem, um POST forjado poderia fazer o OmniClub chamar qualquer endereço (SSRF). Só
    /// aceita https em domínio do TotalPass e no caminho de confirmação documentado. (O host de
    /// produção não é documentado — por isso validamos o domínio em vez de fixar um host.)
    /// </summary>
    public static Uri ValidateConfirmationUrl(string confirmationUrl)
    {
        if (!Uri.TryCreate(confirmationUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !(uri.Host == "totalpass.com" || uri.Host.EndsWith(".totalpass.com", StringComparison.OrdinalIgnoreCase))
            || !uri.AbsolutePath.Contains("/webhook_confirmations/", StringComparison.Ordinal))
        {
            throw new TotalPassException($"URL de confirmação fora do TotalPass recusada: '{Truncate(confirmationUrl)}'.");
        }
        return uri;
    }

    /// <summary>Corpo documentado de sucesso: <c>1</c> (às vezes como string JSON <c>"1"</c>);
    /// aceita também <c>true</c>.</summary>
    private static bool IsConfirmationBody(string body)
    {
        var value = body.Trim().Trim('"').Trim();
        return value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadValidationMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0
                && errors[0].TryGetProperty("source", out var source))
            {
                return source.TryGetProperty("message", out var msg) ? msg.GetString()
                    : source.TryGetProperty("label", out var label) ? label.GetString() : null;
            }
        }
        catch (JsonException)
        {
            // corpo fora do formato documentado — cai na mensagem genérica
        }
        return null;
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
            throw new TotalPassException("Integração TotalPass não configurada no servidor (TotalPass:PartnerApiKey ausente).");
    }

    private Uri Url(string path) => new(new Uri(_options.GymServiceBaseUrl.TrimEnd('/') + "/"), path.TrimStart('/'));

    private static string Truncate(string s) => s.Length > 200 ? s[..200] + "…" : s;

    private record AuthRequest(
        [property: JsonPropertyName("place_api_key")] string PlaceApiKey,
        [property: JsonPropertyName("partner_api_key")] string PartnerApiKey);

    // Formato real (2026-11-11): {"partner":{"name"},"place":{"name","plans":[{"placeId",...}]},"token"}
    // — o host de booking manda "Plans" com P maiúsculo; desserialização case-insensitive cobre os dois.
    private record AuthResponse(AuthPlace? Place, string? Token);
    private record AuthPlace(string? Name, List<AuthPlan>? Plans);
    private record AuthPlan(long? PlaceId, string? Code);

    private record WebhookRegistration(
        [property: JsonPropertyName("webhook_url")] string WebhookUrl,
        [property: JsonPropertyName("webhook_type")] string WebhookType);
}
