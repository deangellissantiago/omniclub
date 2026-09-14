using Checkin.Application.DTOs.Checkins;
using Checkin.Application.DTOs.Dashboard;
using Checkin.Application.Ports;
using Checkin.Application.Ports.Repositories;
using Checkin.Domain.Enums;

namespace Checkin.Application.UseCases.Dashboard;

public class DashboardService
{
    private readonly ICheckinRecordRepository _checkinRepository;
    private readonly IStudentRepository _studentRepository;
    private readonly ICheckinPointRepository _checkinPointRepository;
    private readonly ICurrentTenantContext _tenantContext;

    public DashboardService(
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

    public async Task<DashboardSummaryDto> GetAsync(CancellationToken ct = default)
    {
        var tenantId = _tenantContext.TenantId;
        var apps = Enum.GetValues<IntegrationApp>();

        var checkinsByApp = new List<AppCountDto>();
        var studentsByApp = new List<AppCountDto>();
        foreach (var app in apps)
        {
            checkinsByApp.Add(new AppCountDto(app.ToString(), await _checkinRepository.CountByAppAsync(tenantId, app, null, null, ct)));
            studentsByApp.Add(new AppCountDto(app.ToString(), await _studentRepository.CountByAppAsync(tenantId, app, ct)));
        }

        var totalCheckins = await _checkinRepository.CountAsync(tenantId, null, null, ct);
        var students = await _studentRepository.ListAsync(tenantId, ct);
        var recent = await _checkinRepository.ListRecentAsync(tenantId, 20, ct);
        var points = await _checkinPointRepository.ListAsync(tenantId, ct);

        var recentDtos = recent.Select(r =>
        {
            var student = r.StudentId is not null ? students.FirstOrDefault(s => s.Id == r.StudentId) : null;
            var point = points.FirstOrDefault(p => p.Id == r.CheckinPointId);
            return new CheckinDto(r.Id, r.StudentId, student?.Name, r.CheckinPointId, point?.Name, r.App, r.OccurredAt, r.Status);
        }).ToList();

        return new DashboardSummaryDto(checkinsByApp, studentsByApp, totalCheckins, students.Count, recentDtos);
    }
}
