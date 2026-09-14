namespace Checkin.Application.DTOs.Reports;

/// <summary>Uma célula do heatmap dia-da-semana × hora — quantos check-ins aconteceram naquele
/// cruzamento, no período filtrado. Ajuda a academia a dimensionar equipe/quadra nos horários
/// de pico reais, em vez de achismo.</summary>
public record PeakHourCellDto(DayOfWeek DayOfWeek, int Hour, long TotalCheckins);

public record PeakHoursReportDto(IReadOnlyList<PeakHourCellDto> Cells);
