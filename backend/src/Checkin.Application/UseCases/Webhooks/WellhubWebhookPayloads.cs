using System.Text.Json;
using System.Text.Json.Serialization;

namespace Checkin.Application.UseCases.Webhooks;

// Tipos de payload do webhook do Wellhub (Check-in + Booking, mesma "URL Única" — ver
// WellhubWebhookController). Moveram pra cá (de dentro do controller) junto com o processamento
// em background — a Api não desserializa payload de negócio mais, só grava o corpo cru.

public record EventEnvelope([property: JsonPropertyName("event_type")] string? EventType);

// Payload de booking — confirmado em 2026-09-10 contra os exemplos reais da collection do
// Postman do parceiro ("Old - Gympass Quick Start Guide" > "Webhook Events"). Os 3 eventos
// (booking-requested/-canceled/-late-canceled) compartilham o mesmo formato de event_data; só
// booking-requested manda name/email/phone_number do usuário (phone_number visto ao vivo no
// Sandbox em 2026-09-28).
public record BookingWebhookPayload(
    [property: JsonPropertyName("event_type")] string EventType,
    [property: JsonPropertyName("event_data")] BookingEventData? EventData);

public record BookingEventData(
    [property: JsonPropertyName("user")] BookingUser? User,
    [property: JsonPropertyName("slot")] BookingSlot? Slot,
    [property: JsonPropertyName("timestamp")] long Timestamp,
    [property: JsonPropertyName("event_id")] string? EventId);

public record BookingUser(
    [property: JsonPropertyName("unique_token")] string? UniqueToken,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("phone_number")] string? PhoneNumber = null);

public record BookingSlot(
    [property: JsonPropertyName("id")] long? Id,
    [property: JsonPropertyName("gym_id")] long? GymId,
    [property: JsonPropertyName("class_id")] long? ClassId,
    [property: JsonPropertyName("booking_number")] string? BookingNumber);

// Payload de check-in exatamente como documentado (nomes de campo em snake_case).
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

/// <summary>Só lê o "event_type" do corpo cru, sem desserializar o payload inteiro — usado pelo
/// controller pra decidir se vale gravar o evento (recebido 202) antes de saber qual handler vai
/// tratar (isso só é decidido depois, em WebhookProcessingService).</summary>
public static class WebhookEventReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Lança JsonException se o corpo não for um JSON válido (o chamador decide o que
    /// fazer — hoje, 400). Retorna null se for JSON válido mas sem "event_type".</summary>
    public static string? ReadEventType(string rawBody) =>
        JsonSerializer.Deserialize<EventEnvelope>(rawBody, JsonOptions)?.EventType;
}
