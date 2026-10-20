using Checkin.Application.Ports.Repositories;
using Checkin.Domain.Entities;
using Checkin.Domain.Enums;
using MongoDB.Driver;

namespace Checkin.Infrastructure.Persistence.Mongo.Repositories;

public class OutboundWebhookDeliveryRepository : IOutboundWebhookDeliveryRepository
{
    private readonly MongoContext _context;

    public OutboundWebhookDeliveryRepository(MongoContext context) => _context = context;

    public async Task<OutboundWebhookDelivery> CreateAsync(OutboundWebhookDelivery delivery, CancellationToken ct = default)
    {
        await _context.OutboundWebhookDeliveries.InsertOneAsync(delivery, cancellationToken: ct);
        return delivery;
    }

    public async Task<OutboundWebhookDelivery?> GetByIdAsync(string tenantId, string id, CancellationToken ct = default) =>
        await _context.OutboundWebhookDeliveries.Find(d => d.TenantId == tenantId && d.Id == id).FirstOrDefaultAsync(ct);

    public async Task<bool> ExistsForEventAsync(string tenantId, string eventId, CancellationToken ct = default) =>
        await _context.OutboundWebhookDeliveries.Find(d => d.TenantId == tenantId && d.EventId == eventId).AnyAsync(ct);

    public async Task<IReadOnlyList<OutboundWebhookDelivery>> ListDueAsync(int maxCount, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var builder = Builders<OutboundWebhookDelivery>.Filter;
        var filter = builder.Eq(d => d.Status, OutboundWebhookStatus.Pending)
            & (builder.Eq(d => d.NextAttemptAt, null) | builder.Lte(d => d.NextAttemptAt, now));

        return await _context.OutboundWebhookDeliveries.Find(filter)
            .SortBy(d => d.CreatedAt)
            .Limit(maxCount)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<OutboundWebhookDelivery>> ListRecentAsync(
        string tenantId, int maxCount, OutboundWebhookStatus? status = null, CancellationToken ct = default)
    {
        var builder = Builders<OutboundWebhookDelivery>.Filter;
        var filter = builder.Eq(d => d.TenantId, tenantId);
        if (status.HasValue) filter &= builder.Eq(d => d.Status, status.Value);

        return await _context.OutboundWebhookDeliveries.Find(filter)
            .SortByDescending(d => d.CreatedAt)
            .Limit(maxCount)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<OutboundWebhookDelivery>> ListByIdsAsync(string tenantId, IReadOnlyCollection<string> ids, CancellationToken ct = default) =>
        await _context.OutboundWebhookDeliveries
            .Find(Builders<OutboundWebhookDelivery>.Filter.Eq(d => d.TenantId, tenantId)
                  & Builders<OutboundWebhookDelivery>.Filter.In(d => d.Id, ids))
            .ToListAsync(ct);

    public async Task<bool> UpdateAsync(OutboundWebhookDelivery delivery, CancellationToken ct = default)
    {
        var result = await _context.OutboundWebhookDeliveries.ReplaceOneAsync(
            d => d.TenantId == delivery.TenantId && d.Id == delivery.Id, delivery, cancellationToken: ct);
        return result.ModifiedCount > 0;
    }
}
