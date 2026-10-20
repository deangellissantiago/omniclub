using Checkin.Application.Ports.Repositories;
using Checkin.Domain.Entities;
using Checkin.Domain.Enums;

namespace Checkin.Tests.Fakes;

public class FakeOutboundWebhookDeliveryRepository : IOutboundWebhookDeliveryRepository
{
    public List<OutboundWebhookDelivery> Deliveries { get; } = new();

    public Task<OutboundWebhookDelivery> CreateAsync(OutboundWebhookDelivery delivery, CancellationToken ct = default)
    {
        Deliveries.Add(delivery);
        return Task.FromResult(delivery);
    }

    public Task<OutboundWebhookDelivery?> GetByIdAsync(string tenantId, string id, CancellationToken ct = default) =>
        Task.FromResult(Deliveries.FirstOrDefault(d => d.TenantId == tenantId && d.Id == id));

    public Task<bool> ExistsForEventAsync(string tenantId, string eventId, CancellationToken ct = default) =>
        Task.FromResult(Deliveries.Any(d => d.TenantId == tenantId && d.EventId == eventId));

    public Task<IReadOnlyList<OutboundWebhookDelivery>> ListDueAsync(int maxCount, CancellationToken ct = default) =>
        Task.FromResult((IReadOnlyList<OutboundWebhookDelivery>)Deliveries
            .Where(d => d.Status == OutboundWebhookStatus.Pending && (d.NextAttemptAt is null || d.NextAttemptAt <= DateTime.UtcNow))
            .OrderBy(d => d.CreatedAt).Take(maxCount).ToList());

    public Task<IReadOnlyList<OutboundWebhookDelivery>> ListRecentAsync(
        string tenantId, int maxCount, OutboundWebhookStatus? status = null, CancellationToken ct = default) =>
        Task.FromResult((IReadOnlyList<OutboundWebhookDelivery>)Deliveries
            .Where(d => d.TenantId == tenantId && (status == null || d.Status == status))
            .OrderByDescending(d => d.CreatedAt).Take(maxCount).ToList());

    public Task<IReadOnlyList<OutboundWebhookDelivery>> ListByIdsAsync(string tenantId, IReadOnlyCollection<string> ids, CancellationToken ct = default) =>
        Task.FromResult((IReadOnlyList<OutboundWebhookDelivery>)Deliveries.Where(d => d.TenantId == tenantId && ids.Contains(d.Id)).ToList());

    public Task<bool> UpdateAsync(OutboundWebhookDelivery delivery, CancellationToken ct = default) => Task.FromResult(true);
}
