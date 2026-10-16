using Checkin.Domain.Enums;

namespace Checkin.Domain.Entities;

/// <summary>
/// Um evento que o OmniClub envia para o sistema do cliente (webhook de saída) — hoje só
/// <c>checkin.aprovado</c>. Gravado na hora em que o check-in é aprovado e enviado depois, em
/// background (OutboundWebhookHostedService), pra que um destino lento ou fora do ar nunca atrase
/// nem derrube o processamento do check-in em si.
///
/// O corpo é congelado na criação: todo reenvio manda exatamente o mesmo JSON, com o mesmo
/// <c>id</c> (<see cref="EventId"/>), pra que o destino consiga deduplicar. Já URL e segredo são
/// lidos do Tenant no momento de cada envio — corrigir a configuração no painel corrige também os
/// envios pendentes.
/// </summary>
public class OutboundWebhookDelivery
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = string.Empty;

    /// <summary>O <c>id</c> do evento dentro do payload — hoje, o id do CheckinRecord.</summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>O <c>tipo</c> do evento dentro do payload (ex.: "checkin.aprovado").</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>Corpo JSON exato que é enviado (e assinado) em toda tentativa.</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>Só para exibição no painel (quem foi o aluno do evento).</summary>
    public string? Summary { get; set; }

    public OutboundWebhookStatus Status { get; set; } = OutboundWebhookStatus.Pending;
    public int Attempts { get; set; }

    /// <summary>Nulo = elegível para enviar já.</summary>
    public DateTime? NextAttemptAt { get; set; }

    public DateTime? LastAttemptAt { get; set; }
    public string? LastTargetUrl { get; set; }
    public int? LastStatusCode { get; set; }
    public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeliveredAt { get; set; }
}
