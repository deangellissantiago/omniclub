using Checkin.Application.UseCases.Students;

namespace Checkin.Application.DTOs.Bookings;

/// <summary>Dados úteis do webhook de reserva do TotalPass (ver WebhookProcessingService).</summary>
/// <param name="EventId"><c>event.id</c> — liga a reserva ao horário (ClassSlot.TotalPassEventId).</param>
/// <param name="SlotId"><c>slot.id</c> — id da reserva no TotalPass.</param>
public record TotalPassBookingData(string EventId, string SlotId, string? Status, TotalPassUserInfo User);
