using System.Text;
using System.Text.Json;
using Checkin.Api.BackgroundJobs;
using Checkin.Application.Ports.Repositories;
using Checkin.Application.UseCases.Webhooks;
using Checkin.Domain.Entities;
using Checkin.Infrastructure.Wellhub;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Checkin.Api.Controllers;

/// <summary>
/// Adapter de entrada (inbound) para o Check-in Webhook do Wellhub — contrato confirmado em
/// https://developers.wellhub.com/product/access-control-api/1.0/check-in-webhook
///
/// Desde que a Booking API entrou em escopo, este é o endpoint único recomendado pelo Wellhub
/// ("URL Única" — ver README) para TODOS os eventos, não só check-in: o nome da rota
/// (`checkins`) ficou como legado.
///
/// <b>Responde rápido, processa depois</b>: só valida a assinatura e grava o corpo cru (rápido)
/// antes de responder <c>202 Accepted</c> — quem interpreta o payload e dispara
/// CheckinService/BookingService de verdade é WebhookProcessingHostedService, em background (ver
/// WebhookEvent, comentário "Por quê"). Isso existe porque o Wellhub reportou timeout: o
/// processamento síncrono antigo fazia chamadas de saída pro próprio Wellhub
/// (/access/v1/validate, PATCH de reserva) antes de responder, e isso passava do tempo que eles
/// esperam (~1s, documentado) — mesmo quando o evento era processado com sucesso do nosso lado.
///
/// Credenciais de Sandbox já configuradas em Wellhub:ApiKey / Wellhub:WebhookSecret (ver .env).
/// Sem Wellhub:WebhookSecret configurado, o endpoint aceita qualquer chamada (útil só para testar
/// localmente).
/// </summary>
[ApiController]
[Route("api/integrations/wellhub")]
public class WellhubWebhookController : ControllerBase
{
    private readonly IWebhookEventRepository _webhookEvents;
    private readonly IWebhookEventSignal _signal;
    private readonly WellhubOptions _options;
    private readonly ILogger<WellhubWebhookController> _logger;

    public WellhubWebhookController(
        IWebhookEventRepository webhookEvents, IWebhookEventSignal signal, IOptions<WellhubOptions> options, ILogger<WellhubWebhookController> logger)
    {
        _webhookEvents = webhookEvents;
        _signal = signal;
        _options = options.Value;
        _logger = logger;
    }

    [HttpPost("checkins")]
    public async Task<IActionResult> ReceiveEvent(CancellationToken ct)
    {
        string rawBody;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
        {
            rawBody = await reader.ReadToEndAsync(ct);
        }

        if (!string.IsNullOrEmpty(_options.WebhookSecret))
        {
            var signature = Request.Headers["X-Gympass-Signature"].ToString();
            if (!WellhubSignature.IsValid(rawBody, _options.WebhookSecret, signature))
            {
                _logger.LogWarning("Webhook do Wellhub recebido com assinatura inválida.");
                return Unauthorized();
            }
        }

        string? eventType;
        try
        {
            eventType = WebhookEventReader.ReadEventType(rawBody);
        }
        catch (JsonException)
        {
            return BadRequest();
        }

        if (eventType is null)
        {
            // JSON válido mas sem "event_type" reconhecível — confirma recebimento (não gera
            // retry do lado do Wellhub), mas não há o que enfileirar.
            return Ok();
        }

        await _webhookEvents.CreateAsync(new WebhookEvent
        {
            Provider = "Wellhub",
            EventType = eventType,
            RawPayload = rawBody,
        }, ct);
        _signal.Notify();

        return Accepted(new { status = "accepted" });
    }
}
