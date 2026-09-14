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

public record ClassDto(string Id, string CheckinPointId, string Name, string? Description, long ProductId, string? ExternalId, bool Active);

public record ClassSlotDto(string Id, string ClassId, DateTime StartsAt, DateTime EndsAt, int Capacity, int BookedCount, string? ExternalId);

/// <summary>Produto (plano/tipo de acesso) configurado para uma unidade no Wellhub — ver
/// IWellhubBookingGateway.ListProductsAsync (GET /setup/v1/gyms/:gym_id/products).</summary>
public record WellhubProductDto(long ProductId, string Name, bool Virtual);
