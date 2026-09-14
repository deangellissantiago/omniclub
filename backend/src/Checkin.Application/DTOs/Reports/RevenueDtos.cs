namespace Checkin.Application.DTOs.Reports;

/// <summary>Repasse estimado de um ponto de check-in no período — check-ins Approved × valor
/// configurado em <see cref="Domain.Entities.CheckinPoint.PricePerCheckinCents"/>.</summary>
public record RevenuePerPointDto(
    string CheckinPointId,
    string CheckinPointName,
    string App,
    long ApprovedCheckins,
    int? PricePerCheckinCents,
    /// <summary>Nulo quando o ponto não tem valor configurado — nunca estimamos em cima de um
    /// número que ninguém informou.</summary>
    long? EstimatedRevenueCents);

/// <summary>Conciliação de repasse Wellhub/TotalPass — o diferencial que só o OmniClub calcula
/// (nenhum sistema de gestão de academia genérico tem acesso a esse dado): quanto a academia
/// deveria receber no período, pra bater com o extrato que o app de benefício manda.</summary>
public record RevenueReportDto(
    long TotalApprovedCheckins,
    /// <summary>Nulo quando nenhum ponto tem valor configurado ainda.</summary>
    long? TotalEstimatedRevenueCents,
    IReadOnlyList<RevenuePerPointDto> ByPoint,
    int PointsWithoutPriceConfigured);
