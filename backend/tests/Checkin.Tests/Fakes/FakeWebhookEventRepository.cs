using Checkin.Application.Ports.Repositories;
using Checkin.Domain.Entities;
using Checkin.Domain.Enums;

namespace Checkin.Tests.Fakes;

public class FakeWebhookEventRepository : IWebhookEventRepository
{
    public List<WebhookEvent> Events { get; } = new();
    public int UpdateCallCount { get; private set; }

    public Task<WebhookEvent> CreateAsync(WebhookEvent webhookEvent, CancellationToken ct = default)
    {
        Events.Add(webhookEvent);
        return Task.FromResult(webhookEvent);
    }

    public Task<IReadOnlyList<WebhookEvent>> ListPendingAsync(int maxCount, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var result = Events
            .Where(e => e.Status == WebhookEventStatus.Pending && (e.NextAttemptAt is null || e.NextAttemptAt <= now))
            .OrderBy(e => e.ReceivedAt)
            .Take(maxCount)
            .ToList();
        return Task.FromResult((IReadOnlyList<WebhookEvent>)result);
    }

    public Task<bool> UpdateAsync(WebhookEvent webhookEvent, CancellationToken ct = default)
    {
        UpdateCallCount++;
        return Task.FromResult(true);
    }
}
