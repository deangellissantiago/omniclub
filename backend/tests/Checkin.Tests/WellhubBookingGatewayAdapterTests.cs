using System.Net;
using System.Net.Http.Json;
using Checkin.Infrastructure.Wellhub;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Checkin.Tests;

/// <summary>
/// Cobre o contrato HTTP da Booking API confirmado em 2026-09-10 contra a collection real do
/// Postman do parceiro (paths com /gyms/:gym_id/, sem X-Gym-Id — diferente da Access Control
/// API — e os campos total_capacity/total_booked/class_id confirmados nos exemplos).
/// </summary>
public class WellhubBookingGatewayAdapterTests
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

    private static WellhubBookingGatewayAdapter BuildAdapter(FakeHandler handler, string apiKey = "sandbox-jwt-token") =>
        new(new HttpClient(handler),
            Options.Create(new WellhubOptions { BaseUrl = "https://apitesting.partners.gympass.com", ApiKey = apiKey, WebhookSecret = "x" }),
            NullLogger<WellhubBookingGatewayAdapter>.Instance);

    [Fact]
    public async Task Does_not_call_the_network_when_not_configured()
    {
        var handler = new FakeHandler();
        var adapter = BuildAdapter(handler, apiKey: "");

        var classId = await adapter.CreateClassAsync("609", "Tênis Iniciante", null, productId: 1217);

        Assert.Null(classId);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task ListProductsAsync_hits_the_setup_api_path_and_parses_the_real_response_shape()
    {
        var handler = new FakeHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    gym_id = 609,
                    products = new[] { new { product_id = 1217, name = "Outdoor Training", @virtual = false } }
                })
            }
        };
        var adapter = BuildAdapter(handler);

        var products = await adapter.ListProductsAsync("609");

        Assert.NotNull(products);
        Assert.Equal(1217, products!.Single().ProductId);
        Assert.Equal("Outdoor Training", products.Single().Name);
        var req = handler.LastRequest!;
        Assert.Equal(HttpMethod.Get, req.Method);
        Assert.Equal("/setup/v1/gyms/609/products", req.RequestUri!.AbsolutePath);
        Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
    }

    [Fact]
    public async Task CreateClassAsync_posts_to_the_gym_scoped_path_with_the_wrapped_classes_body()
    {
        var handler = new FakeHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { classes = new[] { new { id = 555 } } })
            }
        };
        var adapter = BuildAdapter(handler);

        var externalId = await adapter.CreateClassAsync("609", "Tênis Iniciante", "desc", productId: 1217);

        Assert.Equal("555", externalId);
        var req = handler.LastRequest!;
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("/booking/v1/gyms/609/classes", req.RequestUri!.AbsolutePath);
        Assert.DoesNotContain("X-Gym-Id", req.Headers.Select(h => h.Key)); // gym_id vai na URL, não em header, na Booking API
        Assert.Contains("\"product_id\":1217", handler.LastRequestBody);
        Assert.Contains("\"classes\":[", handler.LastRequestBody);
    }

    [Fact]
    public async Task UpdateSlotVacancyAsync_sends_total_capacity_and_total_booked()
    {
        var handler = new FakeHandler { Response = new HttpResponseMessage(HttpStatusCode.OK) };
        var adapter = BuildAdapter(handler);

        var ok = await adapter.UpdateSlotVacancyAsync("609", "class-1", "slot-1", totalCapacity: 15, totalBooked: 5);

        Assert.True(ok);
        var req = handler.LastRequest!;
        Assert.Equal(HttpMethod.Patch, req.Method);
        Assert.Equal("/booking/v1/gyms/609/classes/class-1/slots/slot-1", req.RequestUri!.AbsolutePath);
        Assert.Contains("\"total_capacity\":15", handler.LastRequestBody);
        Assert.Contains("\"total_booked\":5", handler.LastRequestBody);
    }

    [Fact]
    public async Task UpdateSlotVacancyAsync_returns_false_on_non_success_status()
    {
        var handler = new FakeHandler { Response = new HttpResponseMessage(HttpStatusCode.NotFound) };
        var adapter = BuildAdapter(handler);

        Assert.False(await adapter.UpdateSlotVacancyAsync("609", "class-1", "slot-1", 15, 5));
    }

    [Fact]
    public async Task ConfirmBookingAsync_patches_the_booking_with_class_id_and_status_2()
    {
        var handler = new FakeHandler { Response = new HttpResponseMessage(HttpStatusCode.OK) };
        var adapter = BuildAdapter(handler);

        var ok = await adapter.ConfirmBookingAsync("609", "1234", "BK_ABC123");

        Assert.True(ok);
        var req = handler.LastRequest!;
        Assert.Equal(HttpMethod.Patch, req.Method);
        Assert.Equal("/booking/v1/gyms/609/bookings/BK_ABC123", req.RequestUri!.AbsolutePath);
        Assert.Contains("\"class_id\":1234", handler.LastRequestBody);
        Assert.Contains("\"status\":2", handler.LastRequestBody); // status 2 = confirmado, visto no exemplo real da collection
    }

    [Fact]
    public async Task RejectBookingAsync_returns_false_on_non_success_status()
    {
        var handler = new FakeHandler { Response = new HttpResponseMessage(HttpStatusCode.NotFound) };
        var adapter = BuildAdapter(handler);

        Assert.False(await adapter.RejectBookingAsync("609", "1234", "BK_ABC123"));
    }

    [Fact]
    public async Task RejectBookingAsync_fails_gracefully_when_classExternalId_is_not_numeric()
    {
        var handler = new FakeHandler();
        var adapter = BuildAdapter(handler);

        var ok = await adapter.RejectBookingAsync("609", "nao-e-numero", "BK_ABC123");

        Assert.False(ok);
        Assert.Null(handler.LastRequest); // nem chega a chamar a rede
    }
}
