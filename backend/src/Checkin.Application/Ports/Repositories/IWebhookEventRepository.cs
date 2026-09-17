using Checkin.Domain.Entities;

namespace Checkin.Application.Ports.Repositories;

public interface IWebhookEventRepository
{
    Task<WebhookEvent> CreateAsync(WebhookEvent webhookEvent, CancellationToken ct = default);

    /// <summary>Eventos elegíveis para processar agora (Status Pending, NextAttemptAt nulo ou no
    /// passado), mais antigos primeiro — usado pelo worker em background.</summary>
    Task<IReadOnlyList<WebhookEvent>> ListPendingAsync(int maxCount, CancellationToken ct = default);

    Task<bool> UpdateAsync(WebhookEvent webhookEvent, CancellationToken ct = default);
}
