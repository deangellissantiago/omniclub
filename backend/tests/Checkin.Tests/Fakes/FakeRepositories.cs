using Checkin.Application.Ports;
using Checkin.Application.Ports.Repositories;
using Checkin.Domain.Entities;
using Checkin.Domain.Enums;

namespace Checkin.Tests.Fakes;

/// <summary>
/// Fakes em memória dos repositórios (portas de saída), usados nos testes de UseCases para
/// evitar depender de um MongoDB real. Implementam só o necessário para os testes atuais.
/// </summary>
public class FakeCheckinPointRepository : ICheckinPointRepository
{
    public List<CheckinPoint> Points { get; } = new();

    public Task<CheckinPoint?> GetByIdAsync(string tenantId, string id, CancellationToken ct = default) =>
        Task.FromResult(Points.FirstOrDefault(p => p.TenantId == tenantId && p.Id == id));

    public Task<CheckinPoint?> GetByExternalIdAsync(IntegrationApp app, string externalId, CancellationToken ct = default) =>
        Task.FromResult(Points.FirstOrDefault(p => p.App == app && p.ExternalId == externalId));

    public Task<IReadOnlyList<CheckinPoint>> ListAsync(string tenantId, CancellationToken ct = default) =>
        Task.FromResult((IReadOnlyList<CheckinPoint>)Points.Where(p => p.TenantId == tenantId).ToList());

    public Task<CheckinPoint> CreateAsync(CheckinPoint point, CancellationToken ct = default)
    {
        Points.Add(point);
        return Task.FromResult(point);
    }

    public Task<bool> UpdateAsync(CheckinPoint point, CancellationToken ct = default) => Task.FromResult(true);

    public Task<bool> DeleteAsync(string tenantId, string id, CancellationToken ct = default) => Task.FromResult(true);
}

public class FakeStudentRepository : IStudentRepository
{
    public List<Student> Students { get; } = new();

    public Task<Student?> GetByIdAsync(string tenantId, string id, CancellationToken ct = default) =>
        Task.FromResult(Students.FirstOrDefault(s => s.TenantId == tenantId && s.Id == id));

    public Task<Student?> GetByWellhubMemberIdAsync(string wellhubMemberId, CancellationToken ct = default) =>
        Task.FromResult(Students.FirstOrDefault(s => s.WellhubMemberId == wellhubMemberId));

    public Task<IReadOnlyList<Student>> ListAsync(string tenantId, CancellationToken ct = default) =>
        Task.FromResult((IReadOnlyList<Student>)Students.Where(s => s.TenantId == tenantId).ToList());

    public Task<Student> CreateAsync(Student student, CancellationToken ct = default)
    {
        Students.Add(student);
        return Task.FromResult(student);
    }

    public Task<bool> UpdateAsync(Student student, CancellationToken ct = default) => Task.FromResult(true);

    public Task<bool> DeleteAsync(string tenantId, string id, CancellationToken ct = default) => Task.FromResult(true);

    public Task<long> CountByAppAsync(string tenantId, IntegrationApp app, CancellationToken ct = default) =>
        Task.FromResult(Students.LongCount(s => s.TenantId == tenantId));
}

public class FakeCheckinRecordRepository : ICheckinRecordRepository
{
    public List<CheckinRecord> Records { get; } = new();
    public int CreateCallCount { get; private set; }

    public Task<CheckinRecord> CreateAsync(CheckinRecord record, CancellationToken ct = default)
    {
        CreateCallCount++;
        Records.Add(record);
        return Task.FromResult(record);
    }

    public Task<CheckinRecord?> FindByExternalCheckinIdAsync(string tenantId, string externalCheckinId, CancellationToken ct = default) =>
        Task.FromResult(Records.FirstOrDefault(r => r.TenantId == tenantId && r.ExternalCheckinId == externalCheckinId));

    public Task<IReadOnlyList<CheckinRecord>> ListRecentAsync(string tenantId, int take, CancellationToken ct = default) =>
        Task.FromResult((IReadOnlyList<CheckinRecord>)Records.Where(r => r.TenantId == tenantId).Take(take).ToList());

    public Task<IReadOnlyList<CheckinRecord>> ListAsync(
        string tenantId, DateTime? start, DateTime? end, string? studentId = null, string? checkinPointId = null, CancellationToken ct = default) =>
        Task.FromResult((IReadOnlyList<CheckinRecord>)Records.Where(r => r.TenantId == tenantId).ToList());

    public Task<long> CountAsync(string tenantId, DateTime? start, DateTime? end, CancellationToken ct = default) =>
        Task.FromResult(Records.LongCount(r => r.TenantId == tenantId));

    public Task<long> CountByAppAsync(string tenantId, IntegrationApp app, DateTime? start, DateTime? end, CancellationToken ct = default) =>
        Task.FromResult(Records.LongCount(r => r.TenantId == tenantId && r.App == app));
}

public class FakeCurrentTenantContext : ICurrentTenantContext
{
    public string TenantId { get; set; } = "tenant-1";
    public string AdminId { get; set; } = "admin-1";
}
