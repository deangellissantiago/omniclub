using Checkin.Domain.Enums;

namespace Checkin.Application.DTOs.Bookings;

public record BookingDto(
    string Id,
    string SlotId,
    string? StudentId,
    string? StudentName,
    string GympassId,
    BookingStatus Status,
    DateTime RequestedAt,
    DateTime? RespondedAt);

/// <summary>Linha da tela de Reservas — a reserva já com aluno, aula, horário e unidade
/// resolvidos (ver BookingService.ListBookingsAsync).</summary>
public record BookingListItemDto(
    string Id,
    BookingStatus Status,
    DateTime RequestedAt,
    DateTime? RespondedAt,
    string? StudentId,
    string? StudentName,
    string GympassId,
    string? ClassId,
    string? ClassName,
    string? CheckinPointId,
    string? CheckinPointName,
    DateTime? SlotStartsAt);

public record ClassDto(string Id, string CheckinPointId, string Name, string? Description, long ProductId, string? ExternalId, bool Active);

public record ClassSlotDto(string Id, string ClassId, DateTime StartsAt, DateTime EndsAt, int Capacity, int BookedCount, string? ExternalId);

/// <summary>Produto (plano/tipo de acesso) configurado para uma unidade no Wellhub — ver
/// IWellhubBookingGateway.ListProductsAsync (GET /setup/v1/gyms/:gym_id/products).</summary>
public record WellhubProductDto(long ProductId, string Name, bool Virtual);
