namespace Checkin.Domain.Enums;

public enum WebhookEventStatus
{
    /// <summary>Aguardando (ou pronto pra) processamento.</summary>
    Pending = 1,
    /// <summary>Sendo processado agora — evita que o mesmo evento seja pego duas vezes.</summary>
    Processing = 2,
    Processed = 3,
    /// <summary>Esgotou as tentativas (ver WebhookProcessingService.MaxAttempts) — fica parado
    /// aqui pra investigação manual, não é reprocessado sozinho.</summary>
    Failed = 4,
}
