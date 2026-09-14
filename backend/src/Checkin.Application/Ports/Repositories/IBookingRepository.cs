using Checkin.Domain.Entities;

namespace Checkin.Application.Ports.Repositories;

public interface IBookingRepository
{
    Task<Booking> CreateAsync(Booking booking, CancellationToken ct = default);
    Task<bool> UpdateAsync(Booking booking, CancellationToken ct = default);

    /// <summary>Usado para idempotência: assim como o Check-in Webhook, os webhooks de Booking
    /// podem reenviar o mesmo evento.</summary>
    Task<Booking?> FindByExternalBookingIdAsync(string tenantId, string externalBookingId, CancellationToken ct = default);

    /// <summary>Reservas do tenant no período (filtra por <see cref="Booking.RequestedAt"/>) —
    /// usado pelo relatório de ocupação/no-show de aulas (ver ReportService.AttendanceAsync).</summary>
    Task<IReadOnlyList<Booking>> ListAsync(string tenantId, DateTime? start, DateTime? end, CancellationToken ct = default);
}
