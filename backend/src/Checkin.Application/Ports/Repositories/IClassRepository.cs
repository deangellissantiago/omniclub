using Checkin.Domain.Entities;

namespace Checkin.Application.Ports.Repositories;

public interface IClassRepository
{
    Task<WellhubClass?> GetByIdAsync(string tenantId, string id, CancellationToken ct = default);
    Task<IReadOnlyList<WellhubClass>> ListAsync(string tenantId, CancellationToken ct = default);
    Task<WellhubClass> CreateAsync(WellhubClass wellhubClass, CancellationToken ct = default);
    Task<bool> UpdateAsync(WellhubClass wellhubClass, CancellationToken ct = default);
}
