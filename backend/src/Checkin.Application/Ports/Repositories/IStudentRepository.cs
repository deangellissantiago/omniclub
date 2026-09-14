using Checkin.Domain.Entities;
using Checkin.Domain.Enums;

namespace Checkin.Application.Ports.Repositories;

public interface IStudentRepository
{
    Task<Student?> GetByIdAsync(string tenantId, string id, CancellationToken ct = default);
    Task<Student?> GetByWellhubMemberIdAsync(string wellhubMemberId, CancellationToken ct = default);
    Task<IReadOnlyList<Student>> ListAsync(string tenantId, CancellationToken ct = default);
    Task<Student> CreateAsync(Student student, CancellationToken ct = default);
    Task<bool> UpdateAsync(Student student, CancellationToken ct = default);
    Task<bool> DeleteAsync(string tenantId, string id, CancellationToken ct = default);
    Task<long> CountByAppAsync(string tenantId, IntegrationApp app, CancellationToken ct = default);
}
