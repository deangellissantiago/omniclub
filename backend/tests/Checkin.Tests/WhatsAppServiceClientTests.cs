using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Checkin.Infrastructure.WhatsApp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Checkin.Tests;

/// <summary>
/// Testa o contrato HTTP de saída (path, header X-Api-Key, body) contra um handler fake — sem
/// rede — cobrindo o que o README do whatsapp-service documenta: POST messages/template,
/// {to, templateName, languageCode, parameters}.
/// </summary>
public class WhatsAppServiceClientTests
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

    private static WhatsAppServiceClient BuildClient(HttpMessageHandler handler, string baseUrl = "http://whatsapp-service:8080/api", string apiKey = "chave-123")
    {
        var options = Options.Create(new WhatsAppOptions { BaseUrl = baseUrl, ApiKey = apiKey });
        return new WhatsAppServiceClient(new HttpClient(handler), options, NullLogger<WhatsAppServiceClient>.Instance);
    }

    [Fact]
    public void IsConfigured_is_false_without_a_base_url()
    {
        Assert.False(BuildClient(new FakeHandler(), baseUrl: "").IsConfigured);
    }

    [Fact]
    public void IsConfigured_is_true_with_a_base_url()
    {
        Assert.True(BuildClient(new FakeHandler()).IsConfigured);
    }

    [Fact]
    public async Task SendTemplateAsync_fails_fast_without_calling_the_network_when_not_configured()
    {
        var handler = new FakeHandler();
        var client = BuildClient(handler, baseUrl: "");

        var result = await client.SendTemplateAsync("5531999999999", "aluno_sumido", "pt_BR", ["Ana"]);

        Assert.False(result.Success);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task SendTemplateAsync_posts_to_the_documented_path_with_the_api_key_header()
    {
        var handler = new FakeHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { status = "sent", providerMessageId = "wamid.ABC", message = (string?)null }),
            },
        };
        var client = BuildClient(handler, baseUrl: "http://whatsapp-service:8080/api", apiKey: "minha-chave");

        await client.SendTemplateAsync("5531999999999", "aluno_sumido", "pt_BR", ["Ana", "Escola de Tênis"]);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("http://whatsapp-service:8080/api/messages/template", handler.LastRequest.RequestUri!.ToString());
        Assert.Equal("minha-chave", handler.LastRequest.Headers.GetValues("X-Api-Key").Single());

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal("5531999999999", doc.RootElement.GetProperty("to").GetString());
        Assert.Equal("aluno_sumido", doc.RootElement.GetProperty("templateName").GetString());
        Assert.Equal("Ana", doc.RootElement.GetProperty("parameters")[0].GetString());
    }

    [Fact]
    public async Task SendTemplateAsync_reports_success_when_the_service_answers_sent()
    {
        var handler = new FakeHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { status = "sent", providerMessageId = "wamid.XYZ", message = (string?)null }),
            },
        };
        var client = BuildClient(handler);

        var result = await client.SendTemplateAsync("5531999999999", "aluno_sumido", "pt_BR", []);

        Assert.True(result.Success);
        Assert.Equal("wamid.XYZ", result.ProviderMessageId);
    }

    [Fact]
    public async Task SendTemplateAsync_reports_failure_when_the_service_is_not_configured_upstream()
    {
        var handler = new FakeHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = JsonContent.Create(new { status = "not_configured", providerMessageId = (string?)null, message = "Meta WhatsApp Cloud API não configurada neste ambiente." }),
            },
        };
        var client = BuildClient(handler);

        var result = await client.SendTemplateAsync("5531999999999", "aluno_sumido", "pt_BR", []);

        Assert.False(result.Success);
        Assert.Contains("não configurada", result.Error);
    }

    [Fact]
    public async Task SendTemplateAsync_does_not_throw_when_the_service_is_unreachable()
    {
        var client = BuildClient(new UnreachableHandler());

        var result = await client.SendTemplateAsync("5531999999999", "aluno_sumido", "pt_BR", []);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    private sealed class UnreachableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("Connection refused (simulado)");
    }
}
