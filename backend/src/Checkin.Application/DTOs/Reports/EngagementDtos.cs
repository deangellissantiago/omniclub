namespace Checkin.Application.DTOs.Reports;

/// <summary>Situação de frequência de um aluno "como está agora" (não é filtrável por período —
/// sempre relativo a <see cref="EngagementReportDto.AsOf"/>): quando foi seu último check-in e
/// com que frequência ele tem aparecido nos últimos 30 dias. É o dado por trás de "alunos
/// sumidos" — o principal preditor de cancelamento de plano numa academia.</summary>
public record StudentEngagementDto(
    string StudentId,
    string StudentName,
    /// <summary>Nulo quando o aluno nunca fez nenhum check-in.</summary>
    DateTime? LastCheckinAt,
    /// <summary>Nulo quando o aluno nunca fez nenhum check-in (em vez de um número arbitrariamente
    /// grande, pra não confundir "nunca veio" com "sumiu há muito tempo" na UI).</summary>
    int? DaysSinceLastCheckin,
    long CheckinsLast30Days,
    double AvgCheckinsPerWeek);

public record EngagementReportDto(IReadOnlyList<StudentEngagementDto> Students, DateTime AsOf);
