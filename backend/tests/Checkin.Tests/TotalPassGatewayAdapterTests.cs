using System.Net;
using System.Text;
using Checkin.Application.Ports.Integrations;
using Checkin.Infrastructure.TotalPass;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Checkin.Tests;

/// <summary>
/// Contrato HTTP do TotalPass (sem rede): /partner/auth com as duas chaves, cadastro do webhook
/// (create na primeira vez, update depois), e a confirmação do check-in na URL do evento —
/// inclusive a trava que só aceita URL https de *.totalpass.com.
/// </summary>
public class TotalPassGatewayAdapterTests
{
    private const string PlaceKey = "place-key-123";

    // Resposta real do /partner/auth (2026-11-11), com token encurtado.
    private const string AuthResponse = """
        {"partner":{"name":"OmniClub","partnerApiKey":"x"},
         "place":{"name":"DVP-HML-266","placeApiKey":"y","plans":[{"id":78422,"name":"Plano Portal Developers","code":"R5FK8NDY","placeId":68362}]},
         "token":"jwt-abc"}
        """;

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Url, string? Body, string? Auth)> Calls { get; } = new();
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Calls.Add((request.Method, request.RequestUri!.ToString(), body, request.Headers.Authorization?.ToString()));
            return Respond(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static TotalPassGatewayAdapter Build(ScriptedHandler handler, string partnerKey = "partner-key", TotalPassTokenCache? cache = null) =>
        new(new HttpClient(handler),
            Options.Create(new TotalPassOptions { PartnerApiKey = partnerKey, WebhookBaseUrl = "https://omniclub.run" }),
            cache ?? new TotalPassTokenCache(), NullLogger<TotalPassGatewayAdapter>.Instance);

    private static HttpResponseMessage Default(HttpRequestMessage r, string webhooksJson = "[]") => r.RequestUri!.AbsolutePath switch
    {
        "/partner/auth" => Json(HttpStatusCode.Created, AuthResponse),
        "/partner/webhook/get" => Json(HttpStatusCode.OK, webhooksJson),
        _ => Json(HttpStatusCode.Created, """{"status":"success","code":201}"""),
    };

    [Fact]
    public void Not_configured_without_partner_key() => Assert.False(Build(new ScriptedHandler(), partnerKey: "").IsConfigured);

    [Fact]
    public async Task Connect_authenticates_with_both_keys_and_creates_webhook_when_none_exists()
    {
        var handler = new ScriptedHandler { Respond = r => Default(r) };

        var connection = await Build(handler).ConnectPlaceAsync(PlaceKey, "/api/integrations/totalpass/checkins/abc");

        Assert.Equal("DVP-HML-266", connection.PlaceName);
        Assert.Equal("68362", connection.PlaceId);
        Assert.Equal("https://omniclub.run/api/integrations/totalpass/checkins/abc", connection.WebhookUrl);

        var auth = handler.Calls[0];
        Assert.Equal("https://gym-service-api.totalpass.com/partner/auth", auth.Url);
        Assert.Contains("\"place_api_key\":\"place-key-123\"", auth.Body);
        Assert.Contains("\"partner_api_key\":\"partner-key\"", auth.Body);

        var create = handler.Calls.Last();
        Assert.Equal(HttpMethod.Post, create.Method);
        Assert.EndsWith("/partner/webhook/create", create.Url);
        Assert.Equal("Bearer jwt-abc", create.Auth);
        Assert.Contains("\"webhook_url\":\"https://omniclub.run/api/integrations/totalpass/checkins/abc\"", create.Body);
        Assert.Contains("\"webhook_type\":\"CHECKIN\"", create.Body);
    }

    [Fact]
    public async Task Connect_updates_existing_checkin_webhook_instead_of_creating_another()
    {
        var handler = new ScriptedHandler
        {
            Respond = r => Default(r, """{"webhooks":[{"webhook_url":"https://antigo","webhook_type":"CHECKIN"}]}"""),
        };

        await Build(handler).ConnectPlaceAsync(PlaceKey, "/x");

        var last = handler.Calls.Last();
        Assert.Equal(HttpMethod.Put, last.Method);
        Assert.EndsWith("/partner/webhook/update", last.Url);
    }

    [Fact]
    public async Task Connect_recognizes_existing_webhook_in_the_real_api_format()
    {
        // Formato real devolvido pelo /partner/webhook/get em 2026-11-11 (difere da documentação).
        var handler = new ScriptedHandler
        {
            Respond = r => Default(r, """[{"id":1,"partnerId":9,"placeIdentifier":"x","url":"https://omniclub.run/a","webhookType":"CHECKIN","createdAt":"2026-11-11T17:00:00Z","updatedAt":null,"deletedAt":null}]"""),
        };

        await Build(handler).ConnectPlaceAsync(PlaceKey, "/x");

        var last = handler.Calls.Last();
        Assert.Equal(HttpMethod.Put, last.Method);
        Assert.EndsWith("/partner/webhook/update", last.Url);
        Assert.DoesNotContain(handler.Calls, c => c.Url.EndsWith("/partner/webhook/create"));
    }

    [Fact]
    public async Task Connect_with_rejected_keys_throws_friendly_error()
    {
        var handler = new ScriptedHandler { Respond = _ => Json(HttpStatusCode.Unauthorized, """{"message":"Invalid credentials"}""") };

        var ex = await Assert.ThrowsAsync<TotalPassException>(() => Build(handler).ConnectPlaceAsync(PlaceKey, "/x"));
        Assert.Contains("place_api_key", ex.Message);
    }

    [Fact]
    public async Task Confirm_posts_to_event_url_with_bearer_and_reuses_cached_token()
    {
        const string url = "https://admin.totalpass.com/api/v1/webhook_confirmations/TOKEN==";
        var handler = new ScriptedHandler
        {
            Respond = r => r.RequestUri!.AbsolutePath == "/partner/auth" ? Json(HttpStatusCode.Created, AuthResponse) : Json(HttpStatusCode.OK, "1"),
        };
        var adapter = Build(handler);

        Assert.True((await adapter.ConfirmCheckinAsync(PlaceKey, url)).Confirmed);
        Assert.True((await adapter.ConfirmCheckinAsync(PlaceKey, url)).Confirmed);

        Assert.Single(handler.Calls, c => c.Url.EndsWith("/partner/auth"));
        var confirm = handler.Calls.Last();
        Assert.Equal(HttpMethod.Post, confirm.Method);
        Assert.Equal(url, confirm.Url);
        Assert.Equal("Bearer jwt-abc", confirm.Auth);
    }

    [Fact]
    public async Task Confirm_expired_checkin_returns_rejection_with_totalpass_message()
    {
        var cache = new TotalPassTokenCache();
        cache.Set(PlaceKey, "jwt-abc");
        var handler = new ScriptedHandler
        {
            Respond = _ => Json(HttpStatusCode.UnprocessableEntity,
                """{"errors":[{"status":"422","title":"Validation Error","source":{"message":"Tempo de check-in expirado! Um novo check-in precisa ser feito.","label":"check_in_expired_alert"}}]}"""),
        };

        var result = await Build(handler, cache: cache).ConfirmCheckinAsync(PlaceKey, "https://admin.staging.totalpass.com/api/v1/webhook_confirmations/T");

        Assert.False(result.Confirmed);
        Assert.Equal("Tempo de check-in expirado! Um novo check-in precisa ser feito.", result.RejectionReason);
    }

    [Fact]
    public async Task Confirm_redirect_to_login_page_is_not_treated_as_confirmed()
    {
        // Comportamento real de admin.totalpass.com em 2026-11-11: 303 → login AWS SSO.
        var cache = new TotalPassTokenCache();
        cache.Set(PlaceKey, "jwt-abc");
        var redirect = new HttpResponseMessage(HttpStatusCode.SeeOther);
        redirect.Headers.Location = new Uri("https://us-east-1.signin.aws/platform/authorize");
        var handler = new ScriptedHandler { Respond = _ => redirect };

        var result = await Build(handler, cache: cache).ConfirmCheckinAsync(PlaceKey, "https://admin.totalpass.com/api/v1/webhook_confirmations/T");

        Assert.False(result.Confirmed);
    }

    [Fact]
    public async Task Confirm_200_with_unexpected_body_is_not_treated_as_confirmed()
    {
        var cache = new TotalPassTokenCache();
        cache.Set(PlaceKey, "jwt-abc");
        var handler = new ScriptedHandler
        {
            Respond = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>login</html>", Encoding.UTF8, "text/html") },
        };

        var result = await Build(handler, cache: cache).ConfirmCheckinAsync(PlaceKey, "https://admin.totalpass.com/api/v1/webhook_confirmations/T");

        Assert.False(result.Confirmed);
    }

    [Fact]
    public async Task Confirm_server_error_throws_so_the_event_is_retried()
    {
        var cache = new TotalPassTokenCache();
        cache.Set(PlaceKey, "jwt-abc");
        var handler = new ScriptedHandler { Respond = _ => new HttpResponseMessage(HttpStatusCode.BadGateway) };

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Build(handler, cache: cache).ConfirmCheckinAsync(PlaceKey, "https://admin.totalpass.com/api/v1/webhook_confirmations/T"));
    }

    [Theory]
    [InlineData("https://evil.example.com/api/v1/webhook_confirmations/T")]
    [InlineData("http://admin.totalpass.com/api/v1/webhook_confirmations/T")]
    [InlineData("https://totalpass.com.evil.io/api/v1/webhook_confirmations/T")]
    [InlineData("https://admin.totalpass.com/qualquer/outro/caminho")]
    public async Task Confirm_refuses_urls_outside_totalpass_without_calling_them(string url)
    {
        var handler = new ScriptedHandler();

        var result = await Build(handler).ConfirmCheckinAsync(PlaceKey, url);

        Assert.False(result.Confirmed);
        Assert.Empty(handler.Calls);
    }
}
