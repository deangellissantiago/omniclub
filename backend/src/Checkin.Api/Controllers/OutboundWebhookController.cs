using Checkin.Application.DTOs.Webhooks;
using Checkin.Application.UseCases.Webhooks;
using Checkin.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Checkin.Api.Controllers;

/// <summary>Configuração e log do webhook de saída do tenant (ver OutboundWebhookService) — tela
/// "Webhook" do painel.</summary>
[ApiController]
[Authorize]
[Route("api/settings/webhook")]
public class OutboundWebhookController : ControllerBase
{
    private readonly OutboundWebhookService _service;

    public OutboundWebhookController(OutboundWebhookService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<OutboundWebhookSettingsDto>> Get(CancellationToken ct) =>
        Ok(await _service.GetSettingsAsync(ct));

    [HttpPut]
    public async Task<ActionResult<OutboundWebhookSettingsDto>> Update(UpdateOutboundWebhookSettingsRequest request, CancellationToken ct) =>
        Ok(await _service.UpdateSettingsAsync(request, ct));

    [HttpPost("secret")]
    public async Task<ActionResult<OutboundWebhookSettingsDto>> RegenerateSecret(CancellationToken ct) =>
        Ok(await _service.RegenerateSecretAsync(ct));

    [HttpPost("test")]
    public async Task<ActionResult<OutboundWebhookTestResultDto>> SendTest(CancellationToken ct) =>
        Ok(await _service.SendTestAsync(ct));

    [HttpGet("deliveries")]
    public async Task<ActionResult<IReadOnlyList<OutboundWebhookDeliveryDto>>> ListDeliveries(
        [FromQuery] OutboundWebhookStatus? status, CancellationToken ct) =>
        Ok(await _service.ListDeliveriesAsync(status, ct: ct));

    /// <summary>Reenvio em lote dos envios selecionados no painel.</summary>
    [HttpPost("deliveries/retry")]
    public async Task<ActionResult<RetryOutboundWebhooksResultDto>> RetryMany(RetryOutboundWebhooksRequest request, CancellationToken ct) =>
        Ok(new RetryOutboundWebhooksResultDto(await _service.RetryManyAsync(request.Ids ?? Array.Empty<string>(), ct)));

    [HttpPost("deliveries/{id}/retry")]
    public async Task<ActionResult<OutboundWebhookDeliveryDto>> Retry(string id, CancellationToken ct) =>
        Ok(await _service.RetryAsync(id, ct));
}
