using System.Text;
using System.Text.Json;
using Checkin.Api.BackgroundJobs;
using Checkin.Application.Ports.Repositories;
using Checkin.Application.UseCases.Webhooks;
using Checkin.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace Checkin.Api.Controllers;

/// <summary>
/// Adapter de entrada do webhook de check-in do TotalPass (<c>CHECK_IN_CREATED</c>). Uma URL por
/// unidade: o TotalPass não documenta assinatura nos webhooks, então a chave secreta no caminho
/// (CheckinPoint.TotalPassWebhookKey, cadastrada no TotalPass pelo botão "Conectar") é o que
/// autentica a chamada — chave desconhecida → 404, sem gravar nada.
///
/// Mesmo modelo do Wellhub: grava o corpo cru e responde na hora; a confirmação no TotalPass e o
/// registro do check-in acontecem em background (WebhookProcessingService).
/// </summary>
[ApiController]
[Route("api/integrations/totalpass")]
public class TotalPassWebhookController : ControllerBase
{
    private readonly ICheckinPointRepository _points;
    private readonly IWebhookEventRepository _webhookEvents;
    private readonly IWebhookEventSignal _signal;

    public TotalPassWebhookController(ICheckinPointRepository points, IWebhookEventRepository webhookEvents, IWebhookEventSignal signal)
    {
        _points = points;
        _webhookEvents = webhookEvents;
        _signal = signal;
    }

    [HttpPost("checkins/{webhookKey}")]
    public async Task<IActionResult> ReceiveCheckin(string webhookKey, CancellationToken ct)
    {
        if (await _points.GetByTotalPassWebhookKeyAsync(webhookKey, ct) is null)
            return NotFound();

        string rawBody;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
        {
            rawBody = await reader.ReadToEndAsync(ct);
        }

        string? eventType;
        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            eventType = doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("type", out var type)
                ? type.GetString()
                : null;
        }
        catch (JsonException)
        {
            return BadRequest();
        }

        await _webhookEvents.CreateAsync(new WebhookEvent
        {
            Provider = WebhookProcessingService.TotalPassProvider,
            EventType = eventType ?? "unknown",
            RawPayload = rawBody,
            RouteKey = webhookKey,
        }, ct);
        _signal.Notify();

        return Ok(new { status = "received" });
    }

    /// <summary>Webhook de <b>reservas</b> do TotalPass ("Booking - New" — cadastrado com
    /// POST /partner/webhook/subscribe). O payload não tem campo de tipo: grava com um tipo fixo.</summary>
    [HttpPost("bookings/{webhookKey}")]
    public async Task<IActionResult> ReceiveBooking(string webhookKey, CancellationToken ct)
    {
        if (await _points.GetByTotalPassWebhookKeyAsync(webhookKey, ct) is null)
            return NotFound();

        string rawBody;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
        {
            rawBody = await reader.ReadToEndAsync(ct);
        }

        try
        {
            using var _ = JsonDocument.Parse(rawBody);
        }
        catch (JsonException)
        {
            return BadRequest();
        }

        await _webhookEvents.CreateAsync(new WebhookEvent
        {
            Provider = WebhookProcessingService.TotalPassProvider,
            EventType = WebhookProcessingService.TotalPassBooking,
            RawPayload = rawBody,
            RouteKey = webhookKey,
        }, ct);
        _signal.Notify();

        return Ok(new { status = "received" });
    }
}
