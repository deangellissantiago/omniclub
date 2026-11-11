using Checkin.Domain.Enums;

namespace Checkin.Application.DTOs.CheckinPoints;

public record UpsertCheckinPointRequest(
    IntegrationApp App,
    string ExternalId,
    string Name,
    bool Active,
    /// <summary>Nulo/omitido = "ainda não configurado" (ver CheckinPoint.PricePerCheckinCents).</summary>
    int? PricePerCheckinCents = null,
    /// <summary>Só TotalPass: chave da unidade ("place_api_key"). Nulo/omitido na edição = mantém a
    /// atual (o painel nunca recebe a chave de volta, só os 4 últimos caracteres).</summary>
    string? TotalPassPlaceApiKey = null);
