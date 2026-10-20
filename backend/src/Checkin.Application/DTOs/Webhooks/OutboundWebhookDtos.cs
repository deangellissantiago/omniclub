using Checkin.Domain.Enums;

namespace Checkin.Application.DTOs.Webhooks;

public record OutboundWebhookSettingsDto(string? Url, string? Secret, bool Enabled);

/// <summary><paramref name="Secret"/> nulo = mantém o segredo atual.</summary>
public record UpdateOutboundWebhookSettingsRequest(string? Url, string? Secret);

public record OutboundWebhookDeliveryDto(
    string Id,
    string EventId,
    string EventType,
    string? Summary,
    OutboundWebhookStatus Status,
    int Attempts,
    DateTime CreatedAt,
    DateTime? LastAttemptAt,
    DateTime? NextAttemptAt,
    DateTime? DeliveredAt,
    int? LastStatusCode,
    string? LastError);

public record RetryOutboundWebhooksRequest(IReadOnlyList<string> Ids);

public record RetryOutboundWebhooksResultDto(int Requeued);

public record OutboundWebhookTestResultDto(bool Success, int? StatusCode, string? Error, string Payload, string Signature);
