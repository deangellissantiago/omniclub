using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Checkin.Application.DTOs.Checkins;
using Checkin.Application.UseCases.Bookings;
using Checkin.Application.UseCases.Checkins;
using Checkin.Infrastructure.Wellhub;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Checkin.Api.Controllers;

/// <summary>
/// Adapter de entrada (inbound) para o Check-in Webhook do Wellhub — contrato confirmado em
/// https://developers.wellhub.com/product/access-control-api/1.0/check-in-webhook
///
/// O Wellhub dispara este POST quando um aluno faz check-in pelo próprio app na academia. Isso é
/// só a notificação: o webhook não substitui a validação — pelo fluxo confirmado com o Wellhub
/// Technical Sales, este endpoint apenas registra o pré-check-in e delega a
/// CheckinService.RegisterWellhubCheckinAsync, que chama a Access Control API
/// (`POST /access/v1/validate`) para confirmar se o usuário tem um passe válido antes de liberar
/// o acesso (ver CheckinService).
///
/// Credenciais de Sandbox já configuradas em Wellhub:ApiKey / Wellhub:WebhookSecret (ver .env).
/// Falta só expor esta URL publicamente (`/api/integrations/wellhub/checkins`) e enviar
/// URL + Secret de volta ao Wellhub Technical Sales para eles habilitarem o envio de eventos de
/// verdade (ver README, seção "Integração Wellhub"). Sem Wellhub:WebhookSecret configurado, o
/// endpoint aceita qualquer chamada (útil só para testar localmente).
///
/// Desde que a Booking API entrou em escopo, este é o endpoint único recomendado pelo Wellhub
/// ("URL Única" — ver README) para TODOS os eventos, não só check-in: o nome da rota
/// (`checkins`) ficou como legado, mas `ReceiveEvent` despacha por `event_type` para
/// CheckinService (checkin) ou BookingService (booking-requested/canceled/late-canceled). O
/// payload de booking abaixo (BookingWebhookPayload) reflete os exemplos reais da collection do
/// Postman do parceiro (pasta "Webhook Events") — confirmado em 2026-09-10.
/// </summary>
[ApiController]
[Route("api/integrations/wellhub")]
public class WellhubWebhookController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly CheckinService _checkinService;
    private readonly BookingService _bookingService;
    private readonly WellhubOptions _options;
    private readonly ILogger<WellhubWebhookController> _logger;

    public WellhubWebhookController(
        CheckinService checkinService, BookingService bookingService, IOptions<WellhubOptions> options, ILogger<WellhubWebhookController> logger)
    {
        _checkinService = checkinService;
        _bookingService = bookingService;
        _options = options.Value;
        _logger = logger;
    }

    // Só pra ler o event_type antes de decidir em qual payload tipado desserializar de verdade.
    private record EventEnvelope([property: JsonPropertyName("event_type")] string? EventType);

    // Payload de booking — confirmado em 2026-09-10 contra os exemplos reais da collection do
    // Postman do parceiro ("Old - Gympass Quick Start Guide" > "Webhook Events"), não mais um
    // palpite. Os 3 eventos (booking-requested/-canceled/-late-canceled) compartilham o mesmo
    // formato de event_data; só booking-requested manda name/email do usuário.
    private record BookingWebhookPayload(
        [property: JsonPropertyName("event_type")] string EventType,
        [property: JsonPropertyName("event_data")] BookingEventData? EventData);

    private record BookingEventData(
        [property: JsonPropertyName("user")] BookingUser? User,
        [property: JsonPropertyName("slot")] BookingSlot? Slot,
        [property: JsonPropertyName("timestamp")] long Timestamp,
        [property: JsonPropertyName("event_id")] string? EventId);

    private record BookingUser(
        [property: JsonPropertyName("unique_token")] string? UniqueToken,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("email")] string? Email);

    private record BookingSlot(
        [property: JsonPropertyName("id")] long? Id,
        [property: JsonPropertyName("gym_id")] long? GymId,
        [property: JsonPropertyName("class_id")] long? ClassId,
        [property: JsonPropertyName("booking_number")] string? BookingNumber);

    // Payload exatamente como documentado (nomes de campo em snake_case, ver página do webhook).
    public record CheckinWebhookPayload(
        [property: JsonPropertyName("event_type")] string EventType,
        [property: JsonPropertyName("event_data")] CheckinEventData EventData);

    public record CheckinEventData(
        [property: JsonPropertyName("user")] CheckinUser User,
        [property: JsonPropertyName("gym")] CheckinGym Gym,
        [property: JsonPropertyName("location")] CheckinLocation? Location,
        [property: JsonPropertyName("timestamp")] long Timestamp);

    public record CheckinUser(
        [property: JsonPropertyName("unique_token")] string UniqueToken,
        [property: JsonPropertyName("first_name")] string? FirstName,
        [property: JsonPropertyName("last_name")] string? LastName,
        [property: JsonPropertyName("email")] string? Email,
        [property: JsonPropertyName("phone_number")] string? PhoneNumber);

    public record CheckinGym(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("product")] CheckinProduct? Product);

    public record CheckinProduct(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("description")] string? Description);

    public record CheckinLocation(
        [property: JsonPropertyName("lat")] double? Lat,
        [property: JsonPropertyName("lon")] double? Lon);

    [HttpPost("checkins")]
    public async Task<IActionResult> ReceiveEvent(CancellationToken ct)
    {
        string rawBody;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
        {
            rawBody = await reader.ReadToEndAsync(ct);
        }

        if (!string.IsNullOrEmpty(_options.WebhookSecret))
        {
            var signature = Request.Headers["X-Gympass-Signature"].ToString();
            if (!WellhubSignature.IsValid(rawBody, _options.WebhookSecret, signature))
            {
                _logger.LogWarning("Webhook do Wellhub recebido com assinatura inválida.");
                return Unauthorized();
            }
        }

        EventEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<EventEnvelope>(rawBody, JsonOptions);
        }
        catch (JsonException)
        {
            return BadRequest();
        }

        return envelope?.EventType switch
        {
            "checkin" => await HandleCheckinAsync(rawBody, ct),
            "booking-requested" => await HandleBookingRequestedAsync(rawBody, ct),
            "booking-canceled" => await HandleBookingCanceledAsync(rawBody, lateCancel: false, ct),
            "booking-late-canceled" => await HandleBookingCanceledAsync(rawBody, lateCancel: true, ct),
            // Evento desconhecido/futuro: confirmamos recebimento (200) para não gerar retry, mas
            // não processamos.
            _ => Ok()
        };
    }

    private async Task<IActionResult> HandleCheckinAsync(string rawBody, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<CheckinWebhookPayload>(rawBody, JsonOptions);
        if (payload?.EventData?.User is null || payload.EventData.Gym is null) return Ok();

        var data = payload.EventData;
        var occurredAt = ToUtcDateTime(data.Timestamp);

        // A Wellhub não envia um id de evento; sintetizamos um para permitir idempotência
        // (o webhook tenta de novo até 3x se não responder em 1s).
        var externalCheckinId = $"{data.User.UniqueToken}:{data.Gym.Id}:{data.Timestamp}";

        var userInfo = new WellhubUserInfo(data.User.FirstName, data.User.LastName, data.User.Email, data.User.PhoneNumber);
        var result = await _checkinService.RegisterWellhubCheckinAsync(
            data.Gym.Id.ToString(),
            data.User.UniqueToken,
            externalCheckinId,
            occurredAt,
            rawBody,
            userInfo,
            ct);

        return Ok(result);
    }

    private async Task<IActionResult> HandleBookingRequestedAsync(string rawBody, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<BookingWebhookPayload>(rawBody, JsonOptions);
        var data = payload?.EventData;
        if (data?.User?.UniqueToken is null || data.Slot?.Id is null || data.Slot.BookingNumber is null)
        {
            _logger.LogWarning("Evento booking-requested recebido em formato inesperado — ignorado (ver BookingWebhookPayload).");
            return Ok();
        }

        var result = await _bookingService.HandleBookingRequestedAsync(
            data.Slot.Id.Value.ToString(), data.User.UniqueToken, data.Slot.BookingNumber, ToUtcDateTime(data.Timestamp), rawBody, ct);

        return Ok(result);
    }

    private async Task<IActionResult> HandleBookingCanceledAsync(string rawBody, bool lateCancel, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<BookingWebhookPayload>(rawBody, JsonOptions);
        var data = payload?.EventData;
        if (data?.Slot?.Id is null || data.Slot.BookingNumber is null)
        {
            _logger.LogWarning("Evento booking-canceled/late-canceled recebido em formato inesperado — ignorado (ver BookingWebhookPayload).");
            return Ok();
        }

        var result = await _bookingService.HandleBookingCanceledAsync(data.Slot.Id.Value.ToString(), data.Slot.BookingNumber, lateCancel, ct);
        return Ok(result);
    }

    /// <summary>
    /// A documentação do Wellhub traz exemplos inconsistentes (10 dígitos = segundos em uma
    /// requisição, 13 dígitos = milissegundos em outra). Detectamos pela quantidade de dígitos.
    /// </summary>
    private static DateTime ToUtcDateTime(long timestamp) =>
        timestamp > 9_999_999_999
            ? DateTimeOffset.FromUnixTimeMilliseconds(timestamp).UtcDateTime
            : DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime;
}
