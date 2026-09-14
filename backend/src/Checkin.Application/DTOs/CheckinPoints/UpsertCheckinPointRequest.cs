using Checkin.Domain.Enums;

namespace Checkin.Application.DTOs.CheckinPoints;

public record UpsertCheckinPointRequest(
    IntegrationApp App,
    string ExternalId,
    string Name,
    bool Active);
