using Checkin.Domain.Entities;
using Checkin.Domain.Enums;

namespace Checkin.Application.Ports.Repositories;

public interface ICheckinPointRepository
{
    Task<CheckinPoint?> GetByIdAsync(string tenantId, string id, CancellationToken ct = default);

    /// <summary>Busca global (sem filtro de tenant) por app + id externo — usada pelo webhook de
    /// integração, que recebe eventos sem contexto de tenant autenticado.</summary>
    Task<CheckinPoint?> GetByExternalIdAsync(IntegrationApp app, string externalId, CancellationToken ct = default);

    Task<IReadOnlyList<CheckinPoint>> ListAsync(string tenantId, CancellationToken ct = default);
    Task<CheckinPoint> CreateAsync(CheckinPoint point, CancellationToken ct = default);
    Task<bool> UpdateAsync(CheckinPoint point, CancellationToken ct = default);
    Task<bool> DeleteAsync(string tenantId, string id, CancellationToken ct = default);
}
