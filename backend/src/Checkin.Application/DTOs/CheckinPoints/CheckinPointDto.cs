using Checkin.Application.DTOs.Bookings;
using Checkin.Domain.Enums;

namespace Checkin.Application.DTOs.CheckinPoints;

/// <summary>Unidade com as integrações que ela aceita (<paramref name="Apps"/>). Os campos
/// <paramref name="ExternalId"/>/<paramref name="Products"/>/<paramref name="PricePerCheckinCents"/>
/// são do Wellhub; os do TotalPass ficam em <paramref name="TotalPass"/>.</summary>
public record CheckinPointDto(
    string Id,
    string Name,
    bool Active,
    DateTime CreatedAt,
    IReadOnlyList<IntegrationApp> Apps,
    string ExternalId,
    IReadOnlyList<WellhubProductDto> Products,
    int? PricePerCheckinCents,
    TotalPassStatusDto? TotalPass);

/// <summary>Integração TotalPass da unidade — nunca expõe a place_api_key inteira.</summary>
public record TotalPassStatusDto(
    bool HasPlaceApiKey,
    string? PlaceApiKeyHint,
    DateTime? ConnectedAt,
    string? PlaceName,
    string? PlaceId,
    int? PricePerCheckinCents);
