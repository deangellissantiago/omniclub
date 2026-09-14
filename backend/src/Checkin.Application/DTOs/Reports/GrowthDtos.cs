namespace Checkin.Application.DTOs.Reports;

public record GrowthPeriodDto(DateTime PeriodStart, long NewStudents);

/// <summary>Curva de crescimento da base de alunos — quantos alunos novos por semana/mês,
/// agrupados por <see cref="Domain.Entities.Student.CreatedAt"/>.</summary>
public record GrowthReportDto(string GroupBy, IReadOnlyList<GrowthPeriodDto> Periods);
