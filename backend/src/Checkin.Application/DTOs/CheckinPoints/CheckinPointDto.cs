using Checkin.Application.DTOs.Bookings;
using Checkin.Domain.Enums;

namespace Checkin.Application.DTOs.CheckinPoints;

public record CheckinPointDto(
    string Id,
    IntegrationApp App,
    string ExternalId,
    string Name,
    bool Active,
    DateTime CreatedAt,
    IReadOnlyList<WellhubProductDto> Products,
    int? PricePerCheckinCents);
