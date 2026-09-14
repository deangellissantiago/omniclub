using Checkin.Application.DTOs.Dashboard;
using Checkin.Application.DTOs.Reports;
using Checkin.Application.Ports;
using Checkin.Application.Ports.Repositories;
using Checkin.Domain.Enums;

namespace Checkin.Application.UseCases.Reports;

public class ReportService
{
    private readonly ICheckinRecordRepository _checkinRepository;
    private readonly IStudentRepository _studentRepository;
    private readonly ICheckinPointRepository _checkinPointRepository;
    private readonly ICurrentTenantContext _tenantContext;

    public ReportService(
        ICheckinRecordRepository checkinRepository,
        IStudentRepository studentRepository,
        ICheckinPointRepository checkinPointRepository,
        ICurrentTenantContext tenantContext)
    {
        _checkinRepository = checkinRepository;
        _studentRepository = studentRepository;
        _checkinPointRepository = checkinPointRepository;
        _tenantContext = tenantContext;
    }

    /// <summary>Relatório de check-ins por aluno (todos ou de um aluno específico), filtrável por data.</summary>
    public async Task<IReadOnlyList<StudentReportItemDto>> ByStudentAsync(DateTime? start, DateTime? end, string? studentId, CancellationToken ct = default)
    {
        var records = await _checkinRepository.ListAsync(_tenantContext.TenantId, start, end, studentId, null, ct);
        var students = await _studentRepository.ListAsync(_tenantContext.TenantId, ct);

        return records
            .Where(r => r.StudentId is not null)
            .GroupBy(r => r.StudentId!)
            .Select(g => new StudentReportItemDto(
                g.Key,
                students.FirstOrDefault(s => s.Id == g.Key)?.Name ?? "Aluno não identificado",
                g.LongCount(),
                g.Max(r => (DateTime?)r.OccurredAt)))
            .OrderByDescending(x => x.TotalCheckins)
            .ToList();
    }

    /// <summary>Relatório de check-ins por escola/ponto de check-in, filtrável por data.</summary>
    public async Task<IReadOnlyList<SchoolReportItemDto>> BySchoolAsync(DateTime? start, DateTime? end, string? checkinPointId, CancellationToken ct = default)
    {
        var records = await _checkinRepository.ListAsync(_tenantContext.TenantId, start, end, null, checkinPointId, ct);
        var points = await _checkinPointRepository.ListAsync(_tenantContext.TenantId, ct);

        return records
            .GroupBy(r => r.CheckinPointId)
            .Select(g =>
            {
                var point = points.FirstOrDefault(p => p.Id == g.Key);
                return new SchoolReportItemDto(g.Key, point?.Name ?? "Ponto removido", point?.App.ToString() ?? "-", g.LongCount(), g.Max(r => (DateTime?)r.OccurredAt));
            })
            .OrderByDescending(x => x.TotalCheckins)
            .ToList();
    }

    /// <summary>Relatório geral de check-ins (totais, por app, top alunos e por ponto), filtrável por data.</summary>
    public async Task<GeneralReportDto> GeneralAsync(DateTime? start, DateTime? end, CancellationToken ct = default)
    {
        var total = await _checkinRepository.CountAsync(_tenantContext.TenantId, start, end, ct);

        var byApp = new List<AppCountDto>();
        foreach (var app in Enum.GetValues<IntegrationApp>())
        {
            byApp.Add(new AppCountDto(app.ToString(), await _checkinRepository.CountByAppAsync(_tenantContext.TenantId, app, start, end, ct)));
        }

        var topStudents = (await ByStudentAsync(start, end, null, ct)).Take(10).ToList();
        var byCheckinPoint = await BySchoolAsync(start, end, null, ct);

        return new GeneralReportDto(total, byApp, topStudents, byCheckinPoint);
    }
}
