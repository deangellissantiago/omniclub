using Checkin.Domain.Enums;

namespace Checkin.Application.DTOs.Reports;

public record BookingStatusCountDto(BookingStatus Status, long Total);

/// <summary>Ocupação/comparecimento das aulas reservadas via Booking API, no período filtrado
/// (filtra por <see cref="Domain.Entities.Booking.RequestedAt"/>).</summary>
public record AttendanceReportDto(
    long TotalBookings,
    IReadOnlyList<BookingStatusCountDto> ByStatus,
    /// <summary>Confirmed / (Confirmed + Rejected) — das reservas já decididas, quantas tinham
    /// vaga. Não considera Requested (ainda sem resposta) nem Canceled/LateCanceled (decididas
    /// como Confirmed antes de serem canceladas).</summary>
    double OccupancyRate,
    /// <summary>LateCanceled / (Confirmed + LateCanceled) — das reservas confirmadas, quantas o
    /// aluno cancelou tarde demais para liberar a vaga pra outra pessoa.</summary>
    double NoShowRate);
