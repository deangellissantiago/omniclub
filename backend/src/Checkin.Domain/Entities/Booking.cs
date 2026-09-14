using Checkin.Domain.Enums;

namespace Checkin.Domain.Entities;

/// <summary>
/// Reserva de um aluno Wellhub em uma <see cref="ClassSlot"/> — "Gestão de Reservas" no fluxo do
/// Wellhub Technical Sales: chega via webhook (booking-requested/booking-canceled/
/// booking-late-canceled) e é respondida via PATCH Update, sem aprovação manual (mesma regra
/// de negócio do check-in: aprovação/rejeição sempre automática, aqui baseada em ter vaga ou não).
/// </summary>
public class Booking
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = string.Empty;
    public string SlotId { get; set; } = string.Empty;

    /// <summary>Nulo quando a reserva não pôde ser casada com nenhum aluno cadastrado.</summary>
    public string? StudentId { get; set; }

    /// <summary>Wellhub ID do usuário que reservou (user.unique_token do payload do webhook).</summary>
    public string GympassId { get; set; } = string.Empty;

    /// <summary>Id da reserva no Wellhub — usado para idempotência e para responder via PATCH Update.</summary>
    public string ExternalBookingId { get; set; } = string.Empty;

    public BookingStatus Status { get; set; } = BookingStatus.Requested;
    public DateTime RequestedAt { get; set; }
    public DateTime? RespondedAt { get; set; }

    public string? RawPayload { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
