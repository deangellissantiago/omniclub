namespace Checkin.Domain.Enums;

public enum OutboundWebhookStatus
{
    /// <summary>Aguardando envio (primeira tentativa ou reenvio agendado).</summary>
    Pending = 1,
    /// <summary>O destino respondeu 2xx.</summary>
    Delivered = 2,
    /// <summary>Esgotou as tentativas (ver OutboundWebhookService.RetrySchedule) — só volta a ser
    /// enviado se alguém pedir o reenvio manual pelo painel.</summary>
    Failed = 3,
}
