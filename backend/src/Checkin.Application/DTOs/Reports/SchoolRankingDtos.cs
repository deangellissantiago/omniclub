namespace Checkin.Application.DTOs.Reports;

public record SchoolRankingItemDto(
    string CheckinPointId,
    string CheckinPointName,
    string App,
    long TotalCheckins,
    long PreviousPeriodCheckins,
    /// <summary>Nulo quando o período anterior não teve nenhum check-in (variação percentual
    /// não faz sentido matematicamente — em vez de exibir "infinito", a UI mostra "novo").</summary>
    double? ChangePercent);

/// <summary>Ranking de unidades com variação período a período (não só total absoluto, como
/// o relatório "Por escola" já mostra) — compara o período pedido com um período anterior de
/// mesma duração, logo antes dele.</summary>
public record SchoolRankingReportDto(
    DateTime PeriodStart,
    DateTime PeriodEnd,
    IReadOnlyList<SchoolRankingItemDto> Items);
