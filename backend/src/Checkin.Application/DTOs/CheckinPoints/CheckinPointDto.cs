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
    int? PricePerCheckinCents,
    TotalPassStatusDto? TotalPass = null);

/// <summary>Situação da integração TotalPass da unidade — nunca expõe a place_api_key inteira.</summary>
public record TotalPassStatusDto(bool HasPlaceApiKey, string? PlaceApiKeyHint, DateTime? ConnectedAt, string? PlaceName);
