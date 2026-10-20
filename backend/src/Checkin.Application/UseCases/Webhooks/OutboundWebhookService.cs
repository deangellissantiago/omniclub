using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Checkin.Application.DTOs.Webhooks;
using Checkin.Application.Ports;
using Checkin.Application.Ports.Integrations;
using Checkin.Application.Ports.Repositories;
using Checkin.Domain.Entities;
using Checkin.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Checkin.Application.UseCases.Webhooks;

/// <summary>
/// Webhook de saída do OmniClub: avisa o sistema do cliente (ex.: o sistema de gestão do clube,
/// que dá presença, crédito e pontos ao atleta) a cada check-in aprovado.
///
/// Contrato (o mesmo que o sistema do cliente já esperava, ver README "Webhook de saída"):
/// POST JSON com header <c>X-Omniclub-Signature: sha256=&lt;hex minúsculo&gt;</c> = HMAC-SHA256
/// do corpo cru com o segredo do tenant. Qualquer 2xx conta como entregue; o resto é reenviado
/// seguindo <see cref="RetrySchedule"/>, sempre com o mesmo corpo e o mesmo <c>id</c>.
/// </summary>
public class OutboundWebhookService
{
    public const string CheckinApprovedEvent = "checkin.aprovado";
    public const string TestEvent = "webhook.teste";

    /// <summary>Espera antes de cada reenvio: 1ª tentativa é imediata; depois 1 min, 5 min, 30 min,
    /// 2 h e 6 h — 6 tentativas no total, cobrindo quase 9 h de destino fora do ar.</summary>
    public static readonly TimeSpan[] RetrySchedule =
    {
        TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(2), TimeSpan.FromHours(6),
    };

    public static int MaxAttempts => RetrySchedule.Length + 1;

    // Acentos literais ("Lúcia", não "L\u00FAcia"): o corpo nunca vai parar dentro de HTML, e fica
    // legível no log de quem recebe.
    private static readonly JsonSerializerOptions PayloadJson = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IOutboundWebhookDeliveryRepository _deliveries;
    private readonly ITenantRepository _tenants;
    private readonly IOutboundWebhookSender _sender;
    private readonly ICurrentTenantContext _tenantContext;
    private readonly ILogger<OutboundWebhookService> _logger;

    public OutboundWebhookService(
        IOutboundWebhookDeliveryRepository deliveries, ITenantRepository tenants, IOutboundWebhookSender sender,
        ICurrentTenantContext tenantContext, ILogger<OutboundWebhookService> logger)
    {
        _deliveries = deliveries;
        _tenants = tenants;
        _sender = sender;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    // ---- Assinatura ------------------------------------------------------------------------

    /// <summary>Valor do header X-Omniclub-Signature: <c>sha256=</c> + HMAC-SHA256(corpo, segredo)
    /// em hex minúsculo.</summary>
    public static string ComputeSignature(string body, string secret)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body));
        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string GenerateSecret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    // ---- Enfileiramento (chamado pelo CheckinService) ----------------------------------------

    /// <summary>Grava o evento <c>checkin.aprovado</c> para envio em background. Não faz nada se o
    /// check-in não foi aprovado, se o tenant não configurou URL + segredo, ou se o evento desse
    /// check-in já foi gravado (idempotente — pode ser chamado de novo no reprocessamento do
    /// webhook do Wellhub sem duplicar o envio).</summary>
    public async Task EnqueueCheckinApprovedAsync(CheckinRecord record, Student? student, CheckinPoint point, CancellationToken ct = default)
    {
        if (record.Status != CheckinStatus.Approved) return;

        var tenant = await _tenants.GetByIdAsync(record.TenantId, ct);
        if (!IsConfigured(tenant)) return;
        if (await _deliveries.ExistsForEventAsync(record.TenantId, record.Id, ct)) return;

        var payload = BuildPayload(CheckinApprovedEvent, record.Id, record.OccurredAt, record.App.ToString(),
            record.ExternalMemberRef, student, point);

        await _deliveries.CreateAsync(new OutboundWebhookDelivery
        {
            TenantId = record.TenantId,
            EventId = record.Id,
            EventType = CheckinApprovedEvent,
            Payload = payload,
            Summary = $"{student?.Name ?? "Aluno não identificado"} · {point.Name}",
        }, ct);
    }

    // ---- Envio (worker em background) -------------------------------------------------------

    /// <summary>Envia até <paramref name="maxBatchSize"/> eventos vencidos; retorna quantos pegou
    /// (0 = nada pendente agora).</summary>
    public async Task<int> ProcessDueBatchAsync(int maxBatchSize = 20, CancellationToken ct = default)
    {
        var due = await _deliveries.ListDueAsync(maxBatchSize, ct);
        foreach (var delivery in due)
        {
            await SendOneAsync(delivery, ct);
        }
        return due.Count;
    }

    private async Task SendOneAsync(OutboundWebhookDelivery delivery, CancellationToken ct)
    {
        delivery.Attempts++;
        delivery.LastAttemptAt = DateTime.UtcNow;

        var tenant = await _tenants.GetByIdAsync(delivery.TenantId, ct);
        if (!IsConfigured(tenant))
        {
            // Desligaram o webhook depois que o evento foi gerado — não insiste.
            delivery.Status = OutboundWebhookStatus.Failed;
            delivery.LastError = "Webhook desativado no painel antes do envio.";
            delivery.NextAttemptAt = null;
            await _deliveries.UpdateAsync(delivery, ct);
            return;
        }

        delivery.LastTargetUrl = tenant!.OutboundWebhookUrl;
        var result = await SafeSendAsync(tenant.OutboundWebhookUrl!, tenant.OutboundWebhookSecret!, delivery.Payload,
            delivery.EventType, delivery.Id, ct);
        delivery.LastStatusCode = result.StatusCode;

        if (result.Success)
        {
            delivery.Status = OutboundWebhookStatus.Delivered;
            delivery.DeliveredAt = DateTime.UtcNow;
            delivery.NextAttemptAt = null;
            delivery.LastError = null;
        }
        else if (delivery.Attempts >= MaxAttempts)
        {
            delivery.Status = OutboundWebhookStatus.Failed;
            delivery.NextAttemptAt = null;
            delivery.LastError = result.Error;
            _logger.LogError("Webhook de saída {Id} ({EventType}) falhou {Attempts}x — desistindo. Último erro: {Error}",
                delivery.Id, delivery.EventType, delivery.Attempts, result.Error);
        }
        else
        {
            delivery.Status = OutboundWebhookStatus.Pending;
            delivery.NextAttemptAt = DateTime.UtcNow + RetrySchedule[delivery.Attempts - 1];
            delivery.LastError = result.Error;
            _logger.LogWarning("Webhook de saída {Id} ({EventType}) falhou na tentativa {Attempts}/{Max}: {Error} — reenvia às {Next:O}.",
                delivery.Id, delivery.EventType, delivery.Attempts, MaxAttempts, result.Error, delivery.NextAttemptAt);
        }

        await _deliveries.UpdateAsync(delivery, ct);
    }

    /// <summary>Nunca lança: qualquer exceção do adapter vira uma falha de envio comum (e entra
    /// no reenvio), em vez de derrubar o lote inteiro.</summary>
    private async Task<OutboundWebhookSendResult> SafeSendAsync(
        string url, string secret, string body, string eventType, string deliveryId, CancellationToken ct)
    {
        try
        {
            return await _sender.SendAsync(new OutboundWebhookRequest(url, body, ComputeSignature(body, secret), eventType, deliveryId), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return new OutboundWebhookSendResult(false, null, ex.Message);
        }
    }

    // ---- Painel (escopo do tenant logado) ----------------------------------------------------

    public async Task<OutboundWebhookSettingsDto> GetSettingsAsync(CancellationToken ct = default)
    {
        var tenant = await CurrentTenantAsync(ct);
        return ToSettingsDto(tenant);
    }

    public async Task<OutboundWebhookSettingsDto> UpdateSettingsAsync(UpdateOutboundWebhookSettingsRequest request, CancellationToken ct = default)
    {
        var tenant = await CurrentTenantAsync(ct);

        var url = string.IsNullOrWhiteSpace(request.Url) ? null : request.Url.Trim();
        if (url is not null &&
            (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)))
        {
            throw new ArgumentException("Informe uma URL completa, começando com https://.");
        }

        if (request.Secret is not null)
        {
            var secret = request.Secret.Trim();
            if (secret.Length > 0 && secret.Length < 16)
                throw new ArgumentException("O segredo precisa ter pelo menos 16 caracteres — use \"Gerar novo segredo\".");
            tenant.OutboundWebhookSecret = secret.Length == 0 ? null : secret;
        }

        tenant.OutboundWebhookUrl = url;
        // Ativou o webhook sem segredo: gera um, pra nunca enviar sem assinatura.
        if (url is not null && string.IsNullOrEmpty(tenant.OutboundWebhookSecret))
            tenant.OutboundWebhookSecret = GenerateSecret();

        await _tenants.UpdateAsync(tenant, ct);
        return ToSettingsDto(tenant);
    }

    public async Task<OutboundWebhookSettingsDto> RegenerateSecretAsync(CancellationToken ct = default)
    {
        var tenant = await CurrentTenantAsync(ct);
        tenant.OutboundWebhookSecret = GenerateSecret();
        await _tenants.UpdateAsync(tenant, ct);
        return ToSettingsDto(tenant);
    }

    /// <summary>Envio síncrono de um evento <c>webhook.teste</c> (dados fictícios, nada é gravado
    /// nem reenviado) — serve pra validar URL, assinatura e conectividade antes do primeiro
    /// check-in real. Tipo diferente de propósito: o destino não deve dar crédito/presença por ele.</summary>
    public async Task<OutboundWebhookTestResultDto> SendTestAsync(CancellationToken ct = default)
    {
        var tenant = await CurrentTenantAsync(ct);
        if (!IsConfigured(tenant))
            throw new Exceptions.ConflictException("Configure a URL do webhook antes de enviar um teste.");

        var testPoint = new CheckinPoint { App = IntegrationApp.Wellhub, ExternalId = "000000", Name = "Unidade de teste", PricePerCheckinCents = 2500 };
        var testStudent = new Student { Name = "Atleta de Teste", Email = "teste@omniclub.run", Phone = "+5531999999999", WellhubMemberId = "0000000000000" };
        var payload = BuildPayload(TestEvent, $"teste-{Guid.NewGuid()}", DateTime.UtcNow, IntegrationApp.Wellhub.ToString(),
            testStudent.WellhubMemberId, testStudent, testPoint);

        var signature = ComputeSignature(payload, tenant.OutboundWebhookSecret!);
        var result = await SafeSendAsync(tenant.OutboundWebhookUrl!, tenant.OutboundWebhookSecret!, payload, TestEvent, "teste", ct);
        return new OutboundWebhookTestResultDto(result.Success, result.StatusCode, result.Error, payload, signature);
    }

    /// <summary>Limite do reenvio em lote — o painel nunca lista mais do que isso de uma vez.</summary>
    public const int MaxRetryBatch = 200;

    public async Task<IReadOnlyList<OutboundWebhookDeliveryDto>> ListDeliveriesAsync(
        OutboundWebhookStatus? status = null, int maxCount = MaxRetryBatch, CancellationToken ct = default) =>
        (await _deliveries.ListRecentAsync(_tenantContext.TenantId, Math.Clamp(maxCount, 1, MaxRetryBatch), status, ct))
            .Select(ToDeliveryDto).ToList();

    /// <summary>Reenvio manual de um envio (botão na linha) — ver <see cref="RetryManyAsync"/>.</summary>
    public async Task<OutboundWebhookDeliveryDto> RetryAsync(string deliveryId, CancellationToken ct = default)
    {
        var delivery = await _deliveries.GetByIdAsync(_tenantContext.TenantId, deliveryId, ct)
            ?? throw new Exceptions.NotFoundException($"Envio '{deliveryId}' não encontrado.");

        await RequeueAsync(delivery, ct);
        return ToDeliveryDto(delivery);
    }

    /// <summary>Reenvio manual dos envios selecionados no painel: voltam pra fila com as
    /// tentativas zeradas e saem no próximo ciclo do worker (alguns segundos), com o mesmo corpo e
    /// o mesmo <c>id</c> — inclusive os já entregues, se o admin escolher (o destino deduplica pelo
    /// id). Ids de outro tenant ou inexistentes são ignorados; retorna quantos foram reagendados.</summary>
    public async Task<int> RetryManyAsync(IReadOnlyCollection<string> deliveryIds, CancellationToken ct = default)
    {
        var ids = deliveryIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
        if (ids.Count == 0)
            throw new ArgumentException("Selecione pelo menos um envio para reenviar.");
        if (ids.Count > MaxRetryBatch)
            throw new ArgumentException($"Selecione no máximo {MaxRetryBatch} envios por vez.");

        var deliveries = await _deliveries.ListByIdsAsync(_tenantContext.TenantId, ids, ct);
        foreach (var delivery in deliveries)
        {
            await RequeueAsync(delivery, ct);
        }
        return deliveries.Count;
    }

    private async Task RequeueAsync(OutboundWebhookDelivery delivery, CancellationToken ct)
    {
        delivery.Status = OutboundWebhookStatus.Pending;
        delivery.Attempts = 0;
        delivery.NextAttemptAt = null;
        await _deliveries.UpdateAsync(delivery, ct);
    }

    // ---- Helpers ----------------------------------------------------------------------------

    private async Task<Tenant> CurrentTenantAsync(CancellationToken ct) =>
        await _tenants.GetByIdAsync(_tenantContext.TenantId, ct)
        ?? throw new Exceptions.NotFoundException("Tenant não encontrado.");

    private static bool IsConfigured(Tenant? tenant) =>
        !string.IsNullOrWhiteSpace(tenant?.OutboundWebhookUrl) && !string.IsNullOrWhiteSpace(tenant.OutboundWebhookSecret);

    private static string BuildPayload(
        string eventType, string eventId, DateTime occurredAtUtc, string origin, string? wellhubId, Student? student, CheckinPoint point)
    {
        var payload = new CheckinWebhookPayloadOut(
            Id: eventId,
            Tipo: eventType,
            AtletaEmail: student?.Email,
            OcorridoEm: ToSaoPaulo(occurredAtUtc),
            Origem: origin,
            ValorRepasse: point.PricePerCheckinCents is int cents ? new decimal(cents, 0, 0, false, 2) : null,
            WellhubId: wellhubId,
            AtletaNome: student?.Name,
            AtletaTelefone: student?.Phone,
            Unidade: new UnidadeOut(point.ExternalId, point.Name));
        return JsonSerializer.Serialize(payload, PayloadJson);
    }

    private static readonly TimeZoneInfo SaoPaulo = FindSaoPaulo();

    /// <summary>Brasil não tem horário de verão desde 2019 — se a imagem não tiver a base de fusos,
    /// o fixo -03:00 dá o mesmo resultado.</summary>
    private static TimeZoneInfo FindSaoPaulo()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"); }
        catch (Exception) { return TimeZoneInfo.CreateCustomTimeZone("BRT", TimeSpan.FromHours(-3), "BRT", "BRT"); }
    }

    private static DateTimeOffset ToSaoPaulo(DateTime utc)
    {
        var asUtc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        var local = TimeZoneInfo.ConvertTime(new DateTimeOffset(asUtc), SaoPaulo);
        return new DateTimeOffset(local.Ticks - local.Ticks % TimeSpan.TicksPerSecond, local.Offset); // sem frações de segundo
    }

    private static OutboundWebhookSettingsDto ToSettingsDto(Tenant t) =>
        new(t.OutboundWebhookUrl, t.OutboundWebhookSecret, IsConfigured(t));

    private static OutboundWebhookDeliveryDto ToDeliveryDto(OutboundWebhookDelivery d) =>
        new(d.Id, d.EventId, d.EventType, d.Summary, d.Status, d.Attempts, d.CreatedAt, d.LastAttemptAt, d.NextAttemptAt,
            d.DeliveredAt, d.LastStatusCode, d.LastError);

    private record CheckinWebhookPayloadOut(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("tipo")] string Tipo,
        [property: JsonPropertyName("atletaEmail")] string? AtletaEmail,
        [property: JsonPropertyName("ocorridoEm")] DateTimeOffset OcorridoEm,
        [property: JsonPropertyName("origem")] string Origem,
        [property: JsonPropertyName("valorRepasse")] decimal? ValorRepasse,
        [property: JsonPropertyName("wellhubId")] string? WellhubId,
        [property: JsonPropertyName("atletaNome")] string? AtletaNome,
        [property: JsonPropertyName("atletaTelefone")] string? AtletaTelefone,
        [property: JsonPropertyName("unidade")] UnidadeOut Unidade);

    private record UnidadeOut(
        [property: JsonPropertyName("gymId")] string GymId,
        [property: JsonPropertyName("nome")] string Nome);
}
