using Checkin.Domain.Entities;

namespace Checkin.Application.Ports.Repositories;

public interface IClassSlotRepository
{
    Task<ClassSlot?> GetByIdAsync(string tenantId, string id, CancellationToken ct = default);

    /// <summary>Busca global (sem filtro de tenant) por id externo — usada pelo webhook de
    /// Booking, que recebe eventos sem contexto de tenant autenticado (mesmo padrão de
    /// ICheckinPointRepository.GetByExternalIdAsync).</summary>
    Task<ClassSlot?> GetByExternalIdAsync(string externalId, CancellationToken ct = default);

    Task<IReadOnlyList<ClassSlot>> ListByClassAsync(string tenantId, string classId, CancellationToken ct = default);
    Task<ClassSlot> CreateAsync(ClassSlot slot, CancellationToken ct = default);
    Task<bool> UpdateAsync(ClassSlot slot, CancellationToken ct = default);
}
