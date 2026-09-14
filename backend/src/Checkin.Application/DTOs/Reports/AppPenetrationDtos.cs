namespace Checkin.Application.DTOs.Reports;

public record AppPenetrationItemDto(string App, long ActiveStudents, double Percent);

/// <summary>Quanto da base de alunos ativos cada app de benefício representa — "não é filtrável
/// por período", sempre a foto de agora (mesma filosofia do relatório de Engajamento).</summary>
public record AppPenetrationReportDto(long TotalActiveStudents, IReadOnlyList<AppPenetrationItemDto> Items);
