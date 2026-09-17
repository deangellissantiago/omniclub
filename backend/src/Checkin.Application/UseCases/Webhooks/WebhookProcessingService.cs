using System.Text.Json;
using Checkin.Application.DTOs.Checkins;
using Checkin.Application.Ports.Repositories;
using Checkin.Application.UseCases.Bookings;
using Checkin.Application.UseCases.Checkins;
using Checkin.Domain.Entities;
using Checkin.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Checkin.Application.UseCases.Webhooks;

/// <summary>
/// Processa em background os eventos gravados por WellhubWebhookController — mesma lógica de
/// despacho que antes vivia inline no controller (ReceiveEvent), só que agora rodando fora do
/// ciclo de requisição HTTP (ver WebhookEvent, "Por quê").
/// </summary>
public class WebhookProcessingService
{
    /// <summary>Depois disso, o evento vira Failed (parado pra investigação manual) em vez de
    /// tentar de novo pra sempre.</summary>
    public const int MaxAttempts = 5;

    private static readonly JsonSerializerOptions JsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web);

    private readonly IWebhookEventRepository _events;
    private readonly CheckinService _checkinService;
    private readonly BookingService _bookingService;
    private readonly ILogger<WebhookProcessingService> _logger;

    public WebhookProcessingService(
        IWebhookEventRepository events, CheckinService checkinService, BookingService bookingService, ILogger<WebhookProcessingService> logger)
    {
        _events = events;
        _checkinService = checkinService;
        _bookingService = bookingService;
        _logger = logger;
    }

    /// <summary>Processa até <paramref name="maxBatchSize"/> eventos pendentes; retorna quantos
    /// pegou (0 = fila vazia agora — quem chama usa isso pra saber se deve continuar batendo ou
    /// esperar o próximo sinal/tick, ver WebhookProcessingHostedService).</summary>
    public async Task<int> ProcessPendingBatchAsync(int maxBatchSize = 50, CancellationToken ct = default)
    {
        var pending = await _events.ListPendingAsync(maxBatchSize, ct);
        foreach (var evt in pending)
        {
            await ProcessOneAsync(evt, ct);
        }
        return pending.Count;
    }

    private async Task ProcessOneAsync(WebhookEvent evt, CancellationToken ct)
    {
        evt.Status = WebhookEventStatus.Processing;
        evt.Attempts++;
        await _events.UpdateAsync(evt, ct);

        try
        {
            await DispatchAsync(evt, ct);
            evt.Status = WebhookEventStatus.Processed;
            evt.ProcessedAt = DateTime.UtcNow;
            evt.LastError = null;
            evt.NextAttemptAt = null;
        }
        catch (Exception ex)
        {
            evt.LastError = ex.Message;
            if (evt.Attempts >= MaxAttempts)
            {
                evt.Status = WebhookEventStatus.Failed;
                _logger.LogError(ex,
                    "WebhookEvent {Id} ({EventType}) falhou {Attempts}x — desistindo, marcado Failed pra investigação manual.",
                    evt.Id, evt.EventType, evt.Attempts);
            }
            else
            {
                evt.Status = WebhookEventStatus.Pending;
                evt.NextAttemptAt = DateTime.UtcNow + Backoff(evt.Attempts);
                _logger.LogWarning(ex,
                    "WebhookEvent {Id} ({EventType}) falhou na tentativa {Attempts}/{Max} — tenta de novo às {NextAttemptAt:O}.",
                    evt.Id, evt.EventType, evt.Attempts, MaxAttempts, evt.NextAttemptAt);
            }
        }

        await _events.UpdateAsync(evt, ct);
    }

    /// <summary>Backoff exponencial (10s, 20s, 40s, 80s, ...) — evita martelar o Wellhub em loop
    /// apertado se a API deles estiver fora do ar (ver ProcessPendingBatchAsync/hosted service,
    /// que ficam num loop drenando a fila até ela esvaziar).</summary>
    private static TimeSpan Backoff(int attempts) => TimeSpan.FromSeconds(Math.Min(10 * Math.Pow(2, attempts - 1), 300));

    private Task DispatchAsync(WebhookEvent evt, CancellationToken ct) => evt.EventType switch
    {
        "checkin" => HandleCheckinAsync(evt.RawPayload, ct),
        "booking-requested" => HandleBookingRequestedAsync(evt.RawPayload, ct),
        "booking-canceled" => HandleBookingCanceledAsync(evt.RawPayload, lateCancel: false, ct),
        "booking-late-canceled" => HandleBookingCanceledAsync(evt.RawPayload, lateCancel: true, ct),
        // Evento desconhecido/futuro: já foi confirmado (202) na entrada — aqui só marca
        // Processed sem fazer nada, mesma postura do controller original.
        _ => Task.CompletedTask,
    };

    private async Task HandleCheckinAsync(string rawBody, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<CheckinWebhookPayload>(rawBody, JsonOptions);
        if (payload?.EventData?.User is null || payload.EventData.Gym is null)
        {
            _logger.LogWarning("Evento checkin recebido em formato inesperado — ignorado (ver CheckinWebhookPayload).");
            return;
        }

        var data = payload.EventData;
        var occurredAt = ToUtcDateTime(data.Timestamp);

        // A Wellhub não envia um id de evento; sintetizamos um para permitir idempotência
        // (o webhook pode reenviar o mesmo evento).
        var externalCheckinId = $"{data.User.UniqueToken}:{data.Gym.Id}:{data.Timestamp}";

        var userInfo = new WellhubUserInfo(data.User.FirstName, data.User.LastName, data.User.Email, data.User.PhoneNumber);
        await _checkinService.RegisterWellhubCheckinAsync(
            data.Gym.Id.ToString(), data.User.UniqueToken, externalCheckinId, occurredAt, rawBody, userInfo, ct);
    }

    private async Task HandleBookingRequestedAsync(string rawBody, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<BookingWebhookPayload>(rawBody, JsonOptions);
        var data = payload?.EventData;
        if (data?.User?.UniqueToken is null || data.Slot?.Id is null || data.Slot.BookingNumber is null)
        {
            _logger.LogWarning("Evento booking-requested recebido em formato inesperado — ignorado (ver BookingWebhookPayload).");
            return;
        }

        await _bookingService.HandleBookingRequestedAsync(
            data.Slot.Id.Value.ToString(), data.User.UniqueToken, data.Slot.BookingNumber, ToUtcDateTime(data.Timestamp), rawBody, ct);
    }

    private async Task HandleBookingCanceledAsync(string rawBody, bool lateCancel, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<BookingWebhookPayload>(rawBody, JsonOptions);
        var data = payload?.EventData;
        if (data?.Slot?.Id is null || data.Slot.BookingNumber is null)
        {
            _logger.LogWarning("Evento booking-canceled/late-canceled recebido em formato inesperado — ignorado (ver BookingWebhookPayload).");
            return;
        }

        await _bookingService.HandleBookingCanceledAsync(data.Slot.Id.Value.ToString(), data.Slot.BookingNumber, lateCancel, ct);
    }

    /// <summary>A documentação do Wellhub traz exemplos inconsistentes (10 dígitos = segundos em
    /// uma requisição, 13 dígitos = milissegundos em outra). Detectamos pela quantidade de dígitos.</summary>
    private static DateTime ToUtcDateTime(long timestamp) =>
        timestamp > 9_999_999_999
            ? DateTimeOffset.FromUnixTimeMilliseconds(timestamp).UtcDateTime
            : DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime;
}
