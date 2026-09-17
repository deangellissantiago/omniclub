using Checkin.Domain.Enums;

namespace Checkin.Domain.Entities;

/// <summary>
/// Um webhook recebido de um app de benefício (Wellhub, e futuramente TotalPass), gravado cru
/// e processado depois, em background — não durante a requisição HTTP.
///
/// Por quê: o Wellhub espera resposta rápida (documentado: ~1s, com retry se não responder) e o
/// processamento de verdade (RegisterWellhubCheckinAsync/BookingService) faz chamadas de saída
/// pro próprio Wellhub (/access/v1/validate, PATCH de reserva) que podem demorar — se isso tudo
/// acontece antes de responder, o Wellhub pode dar timeout mesmo com o evento processado com
/// sucesso do nosso lado (foi exatamente o que o time deles reportou). Agora
/// WellhubWebhookController só valida a assinatura e grava isto (rápido) antes de responder
/// 202 — quem processa de verdade é WebhookProcessingHostedService, em background.
///
/// Sem tenant no momento do recebimento: o tenant só é conhecido depois que o processamento acha
/// o CheckinPoint pelo gym.id (mesma ordem de hoje, só que adiada).
/// </summary>
public class WebhookEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>De onde veio — hoje só "Wellhub", preparado pro TotalPass no futuro.</summary>
    public string Provider { get; set; } = "Wellhub";

    /// <summary>O "event_type" do payload (checkin, booking-requested, booking-canceled,
    /// booking-late-canceled, ou outro valor desconhecido/futuro — ver WebhookProcessingService).</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>Corpo cru exatamente como recebido — o processamento desserializa isto depois.</summary>
    public string RawPayload { get; set; } = string.Empty;

    public WebhookEventStatus Status { get; set; } = WebhookEventStatus.Pending;

    public int Attempts { get; set; }

    public string? LastError { get; set; }

    /// <summary>Nulo = elegível para processar já. Setado após uma falha, com backoff crescente
    /// (ver WebhookProcessingService), pra não martelar o Wellhub em loop apertado se a API deles
    /// estiver fora do ar.</summary>
    public DateTime? NextAttemptAt { get; set; }

    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
}
