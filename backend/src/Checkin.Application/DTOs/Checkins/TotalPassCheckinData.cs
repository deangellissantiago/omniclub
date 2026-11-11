namespace Checkin.Application.DTOs.Checkins;

/// <summary>Dados úteis do evento <c>CHECK_IN_CREATED</c> do TotalPass (ver
/// WebhookProcessingService / TotalPassCheckinPayload).</summary>
public record TotalPassCheckinData(
    string ConfirmationUrl,
    DateTime StartedAtUtc,
    string? UserCode,
    string? UserName,
    string? UserEmail,
    string? UserPhone,
    string? DocumentNumber,
    string? DocumentType);
