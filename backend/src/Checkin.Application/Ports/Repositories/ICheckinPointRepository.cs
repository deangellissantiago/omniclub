using Checkin.Domain.Entities;
using Checkin.Domain.Enums;

namespace Checkin.Application.Ports.Repositories;

public interface ICheckinPointRepository
{
    Task<CheckinPoint?> GetByIdAsync(string tenantId, string id, CancellationToken ct = default);

    /// <summary>Busca global (sem filtro de tenant) pelo Gym ID do Wellhub — usada pelo webhook do
    /// Wellhub, que recebe eventos sem contexto de tenant autenticado.</summary>
    Task<CheckinPoint?> GetByWellhubGymIdAsync(string gymId, CancellationToken ct = default);

    /// <summary>Busca global pela chave da URL de webhook do TotalPass (ver
    /// CheckinPoint.TotalPassWebhookKey) — o evento chega sem tenant autenticado.</summary>
    Task<CheckinPoint?> GetByTotalPassWebhookKeyAsync(string webhookKey, CancellationToken ct = default);

    Task<IReadOnlyList<CheckinPoint>> ListAsync(string tenantId, CancellationToken ct = default);
    Task<CheckinPoint> CreateAsync(CheckinPoint point, CancellationToken ct = default);
    Task<bool> UpdateAsync(CheckinPoint point, CancellationToken ct = default);
    Task<bool> DeleteAsync(string tenantId, string id, CancellationToken ct = default);
}
