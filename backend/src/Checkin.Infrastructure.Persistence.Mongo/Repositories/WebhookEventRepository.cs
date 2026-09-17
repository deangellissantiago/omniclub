using Checkin.Application.Ports.Repositories;
using Checkin.Domain.Entities;
using Checkin.Domain.Enums;
using MongoDB.Driver;

namespace Checkin.Infrastructure.Persistence.Mongo.Repositories;

public class WebhookEventRepository : IWebhookEventRepository
{
    private readonly MongoContext _context;

    public WebhookEventRepository(MongoContext context) => _context = context;

    public async Task<WebhookEvent> CreateAsync(WebhookEvent webhookEvent, CancellationToken ct = default)
    {
        await _context.WebhookEvents.InsertOneAsync(webhookEvent, cancellationToken: ct);
        return webhookEvent;
    }

    public async Task<IReadOnlyList<WebhookEvent>> ListPendingAsync(int maxCount, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var builder = Builders<WebhookEvent>.Filter;
        var filter = builder.Eq(e => e.Status, WebhookEventStatus.Pending)
            & (builder.Eq(e => e.NextAttemptAt, null) | builder.Lte(e => e.NextAttemptAt, now));

        return await _context.WebhookEvents.Find(filter)
            .SortBy(e => e.ReceivedAt)
            .Limit(maxCount)
            .ToListAsync(ct);
    }

    public async Task<bool> UpdateAsync(WebhookEvent webhookEvent, CancellationToken ct = default)
    {
        var result = await _context.WebhookEvents.ReplaceOneAsync(e => e.Id == webhookEvent.Id, webhookEvent, cancellationToken: ct);
        return result.ModifiedCount > 0;
    }
}
