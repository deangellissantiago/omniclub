using System.Net;
using System.Net.Http.Json;
using Checkin.Infrastructure.Wellhub;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Checkin.Tests;

/// <summary>
/// Testa o contrato HTTP de saída (headers, path, body) contra um handler fake — sem rede —
/// cobrindo o que documentamos em IWellhubGateway/WellhubOptions: host configurável,
/// Authorization: Bearer {ApiKey} + X-Gym-Id: {gym id}, POST /access/v1/validate com
/// {gympass_id, custom_code}.
/// </summary>
public class WellhubGatewayAdapterTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }
        public HttpResponseMessage Response { get; set; } = new(HttpStatusCode.OK);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return Response;
        }
    }

    private static WellhubGatewayAdapter BuildAdapter(FakeHandler handler, string apiKey = "sandbox-jwt-token", string baseUrl = "https://api.partners.gympass.com")
    {
        var options = Options.Create(new WellhubOptions { BaseUrl = baseUrl, ApiKey = apiKey, WebhookSecret = "irrelevante-aqui" });
        var httpClient = new HttpClient(handler);
        return new WellhubGatewayAdapter(httpClient, options, NullLogger<WellhubGatewayAdapter>.Instance);
    }

    [Fact]
    public void IsConfigured_is_false_without_an_api_key()
    {
        var adapter = BuildAdapter(new FakeHandler(), apiKey: "");
        Assert.False(adapter.IsConfigured);
    }

    [Fact]
    public void IsConfigured_is_true_with_an_api_key()
    {
        var adapter = BuildAdapter(new FakeHandler(), apiKey: "sandbox-jwt-token");
        Assert.True(adapter.IsConfigured);
    }

    [Fact]
    public async Task ValidateAccessAsync_sends_bearer_and_gym_id_headers_to_the_documented_path()
    {
        var handler = new FakeHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { results = new { user = new { gympass_id = "gp-1" }, validated_at = (DateTime?)null } })
            }
        };
        var adapter = BuildAdapter(handler);

        var result = await adapter.ValidateAccessAsync("gp-1", customCode: null, gymExternalId: "609");

        Assert.NotNull(result);
        Assert.Equal("gp-1", result!.GympassId);

        var req = handler.LastRequest!;
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("/access/v1/validate", req.RequestUri!.AbsolutePath);
        Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
        Assert.Equal("sandbox-jwt-token", req.Headers.Authorization!.Parameter);
        Assert.Equal("609", req.Headers.GetValues("X-Gym-Id").Single());
        Assert.Contains("\"gympass_id\":\"gp-1\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task ValidateAccessAsync_returns_null_when_wellhub_rejects_the_request()
    {
        var handler = new FakeHandler { Response = new HttpResponseMessage(HttpStatusCode.NotFound) };
        var adapter = BuildAdapter(handler);

        var result = await adapter.ValidateAccessAsync("gp-1", null, "609");

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateAccessAsync_returns_null_without_calling_the_network_when_not_configured()
    {
        var handler = new FakeHandler();
        var adapter = BuildAdapter(handler, apiKey: "");

        var result = await adapter.ValidateAccessAsync("gp-1", null, "609");

        Assert.Null(result);
        Assert.Null(handler.LastRequest);
    }
}
