using Checkin.Domain.Entities;

namespace Checkin.Application.Ports.Repositories;

public interface IOutboundWebhookDeliveryRepository
{
    Task<OutboundWebhookDelivery> CreateAsync(OutboundWebhookDelivery delivery, CancellationToken ct = default);
    Task<OutboundWebhookDelivery?> GetByIdAsync(string tenantId, string id, CancellationToken ct = default);
    Task<bool> ExistsForEventAsync(string tenantId, string eventId, CancellationToken ct = default);

    /// <summary>Busca global (todos os tenants) dos envios elegíveis agora (Pending, NextAttemptAt
    /// nulo ou no passado), mais antigos primeiro — usado pelo worker em background.</summary>
    Task<IReadOnlyList<OutboundWebhookDelivery>> ListDueAsync(int maxCount, CancellationToken ct = default);

    /// <summary>Envios mais recentes do tenant — log exibido no painel.</summary>
    Task<IReadOnlyList<OutboundWebhookDelivery>> ListRecentAsync(string tenantId, int maxCount, CancellationToken ct = default);

    Task<bool> UpdateAsync(OutboundWebhookDelivery delivery, CancellationToken ct = default);
}
