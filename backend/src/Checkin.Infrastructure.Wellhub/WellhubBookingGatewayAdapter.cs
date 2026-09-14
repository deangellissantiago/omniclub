using System.Net.Http.Headers;
using System.Net.Http.Json;
using Checkin.Application.DTOs.Bookings;
using Checkin.Application.Ports.Integrations;
using Checkin.Infrastructure.Wellhub.Dtos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Checkin.Infrastructure.Wellhub;

/// <summary>
/// Adapter de saída para IWellhubBookingGateway. Reaproveita host/credenciais de WellhubOptions
/// (mesmo Bearer token da Access Control API). Paths e nomes de campo confirmados em 2026-09-10
/// contra os requests reais da collection do Postman do parceiro ("Old - Gympass Quick Start
/// Guide - Booking &amp; Access Control API Copy") — ao contrário da v1 desta integração, que
/// chutou <c>/booking/v1/classes</c> (sem <c>/gyms/:gym_id/</c>) e "available_spots" (o campo
/// certo é <c>total_booked</c>/<c>total_capacity</c>).
///
/// Diferença importante da Access Control API: aqui o <c>gym_id</c> vai na URL
/// (<c>/gyms/{gymExternalId}/...</c>), não no header <c>X-Gym-Id</c>.
///
/// Testado ao vivo contra o Sandbox 609 em 2026-09-10: <see cref="ListProductsAsync"/> (200, 2
/// produtos reais), <see cref="CreateClassAsync"/> (201, criou a categoria 16402 de verdade —
/// resposta real: <c>{"classes":[{"id":...,"name":...,"links":[...]}]}</c>, formato não estava
/// na collection, só descobrimos testando) e <see cref="CreateSlotAsync"/> (201, criou o slot
/// 304912 — resposta real: <c>{"metadata":{...},"results":[{"id":...,"class_id":...}]}</c>,
/// mesmo envelope do <c>/access/v1/validate</c>). <see cref="ConfirmBookingAsync"/>/
/// <see cref="RejectBookingAsync"/> e <see cref="UpdateSlotVacancyAsync"/> seguem os exemplos
/// confirmados da collection mas ainda não foram exercitados ao vivo com uma reserva de verdade.
/// </summary>
public class WellhubBookingGatewayAdapter : IWellhubBookingGateway
{
    // Status confirmado na collection (exemplo real do PATCH Validate Booking). O código de
    // "rejeitado" não aparece em nenhum exemplo — 3 é um palpite (ver IWellhubBookingGateway).
    private const int BookingStatusConfirmed = 2;
    private const int BookingStatusRejected = 3;

    private readonly HttpClient _httpClient;
    private readonly WellhubOptions _options;
    private readonly ILogger<WellhubBookingGatewayAdapter> _logger;

    public WellhubBookingGatewayAdapter(HttpClient httpClient, IOptions<WellhubOptions> options, ILogger<WellhubBookingGatewayAdapter> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        if (Uri.TryCreate(_options.BaseUrl, UriKind.Absolute, out var baseUri))
        {
            _httpClient.BaseAddress = baseUri;
        }
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public async Task<IReadOnlyList<WellhubProductDto>?> ListProductsAsync(string gymExternalId, CancellationToken ct = default)
    {
        if (!IsConfigured) return null;

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/setup/v1/gyms/{Uri.EscapeDataString(gymExternalId)}/products");
        ApplyHeaders(request);

        using var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Wellhub GET /setup/v1/gyms/{GymId}/products retornou {Status}", gymExternalId, response.StatusCode);
            return null;
        }

        var body = await response.Content.ReadFromJsonAsync<WellhubProductsResponse>(cancellationToken: ct);
        return body?.Products?.Select(p => new WellhubProductDto(p.ProductId, p.Name, p.Virtual)).ToList();
    }

    public async Task<string?> CreateClassAsync(string gymExternalId, string name, string? description, long productId, CancellationToken ct = default)
    {
        if (!IsConfigured) return null;

        // Testado ao vivo: o Wellhub rejeita description nulo/vazio ("should not be empty" /
        // "expectedType: String") — description é opcional do nosso lado, então caímos pro nome
        // da categoria quando não foi informado.
        var effectiveDescription = string.IsNullOrWhiteSpace(description) ? name : description;

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/booking/v1/gyms/{Uri.EscapeDataString(gymExternalId)}/classes")
        {
            Content = JsonContent.Create(new CreateClassesRequest(new[]
            {
                new CreateClassItem(name, effectiveDescription, Notes: null, Bookable: true, Visible: true, IsVirtual: false, ProductId: productId)
            }))
        };
        ApplyHeaders(request);

        using var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Wellhub POST /booking/v1/gyms/{GymId}/classes retornou {Status} para '{Name}'", gymExternalId, response.StatusCode, name);
            return null;
        }

        var body = await response.Content.ReadFromJsonAsync<CreateClassesResponse>(cancellationToken: ct);
        var id = body?.Classes?.FirstOrDefault()?.Id;
        if (id is null)
        {
            _logger.LogWarning("Wellhub criou a categoria '{Name}' (200/201) mas não consegui extrair o id da resposta — formato inesperado.", name);
        }
        return id?.ToString();
    }

    public async Task<string?> CreateSlotAsync(
        string gymExternalId, string classExternalId, long productId, DateTime startsAt, DateTime endsAt, int capacity, CancellationToken ct = default)
    {
        if (!IsConfigured) return null;

        var lengthInMinutes = Math.Max(1, (int)(endsAt - startsAt).TotalMinutes);
        var slotRequest = new Dtos.CreateSlotRequest(
            OccurDate: startsAt,
            Status: 1, // confirmado como "ativo" nos exemplos de Create/Update slot da collection
            Room: string.Empty,
            LengthInMinutes: lengthInMinutes,
            TotalCapacity: capacity,
            TotalBooked: 0,
            ProductId: productId,
            BookingWindow: new BookingWindow(OpensAt: DateTime.UtcNow, ClosesAt: startsAt),
            CancellableUntil: startsAt,
            Instructors: Array.Empty<object>(),
            Rate: 0m);

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"/booking/v1/gyms/{Uri.EscapeDataString(gymExternalId)}/classes/{Uri.EscapeDataString(classExternalId)}/slots")
        {
            Content = JsonContent.Create(slotRequest)
        };
        ApplyHeaders(request);

        using var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Wellhub POST .../classes/{ClassId}/slots retornou {Status}", classExternalId, response.StatusCode);
            return null;
        }

        var body = await response.Content.ReadFromJsonAsync<CreateSlotResponse>(cancellationToken: ct);
        var id = body?.Results?.FirstOrDefault()?.Id;
        if (id is null)
        {
            _logger.LogWarning("Wellhub criou o slot (200/201) mas não consegui extrair o id da resposta — formato inesperado.");
        }
        return id?.ToString();
    }

    public async Task<bool> UpdateSlotVacancyAsync(
        string gymExternalId, string classExternalId, string slotExternalId, int totalCapacity, int totalBooked, CancellationToken ct = default)
    {
        if (!IsConfigured) return false;

        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            $"/booking/v1/gyms/{Uri.EscapeDataString(gymExternalId)}/classes/{Uri.EscapeDataString(classExternalId)}/slots/{Uri.EscapeDataString(slotExternalId)}")
        {
            Content = JsonContent.Create(new PatchSlotVacancyRequest(totalCapacity, totalBooked))
        };
        ApplyHeaders(request);

        using var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Wellhub PATCH .../classes/{ClassId}/slots/{SlotId} retornou {Status}", classExternalId, slotExternalId, response.StatusCode);
        }
        return response.IsSuccessStatusCode;
    }

    public Task<bool> ConfirmBookingAsync(string gymExternalId, string classExternalId, string bookingNumber, CancellationToken ct = default) =>
        ValidateBookingAsync(gymExternalId, classExternalId, bookingNumber, BookingStatusConfirmed, ct);

    public Task<bool> RejectBookingAsync(string gymExternalId, string classExternalId, string bookingNumber, CancellationToken ct = default) =>
        ValidateBookingAsync(gymExternalId, classExternalId, bookingNumber, BookingStatusRejected, ct);

    private async Task<bool> ValidateBookingAsync(string gymExternalId, string classExternalId, string bookingNumber, int status, CancellationToken ct)
    {
        if (!IsConfigured) return false;
        if (!long.TryParse(classExternalId, out var classId))
        {
            _logger.LogWarning("classExternalId '{ClassExternalId}' não é numérico — Wellhub exige class_id inteiro no PATCH de reserva.", classExternalId);
            return false;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Patch, $"/booking/v1/gyms/{Uri.EscapeDataString(gymExternalId)}/bookings/{Uri.EscapeDataString(bookingNumber)}")
        {
            Content = JsonContent.Create(new ValidateBookingRequest(classId, status))
        };
        ApplyHeaders(request);

        using var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Wellhub PATCH .../bookings/{BookingNumber} (status={BookingStatus}) retornou {HttpStatus}", bookingNumber, status, response.StatusCode);
        }
        return response.IsSuccessStatusCode;
    }

    // Ao contrário da Access Control API, a Booking API não usa X-Gym-Id — o gym_id já vai na URL.
    private void ApplyHeaders(HttpRequestMessage request) =>
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
}
