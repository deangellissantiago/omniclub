using Checkin.Application.DTOs.Billing;
using Checkin.Application.UseCases.Billing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Checkin.Api.Controllers;

/// <summary>
/// Assinatura mensal via Stripe (ver README, "Cobrança / Stripe"). O webhook
/// (<see cref="ReceiveWebhook"/>) é a única rota pública daqui — o Stripe não manda JWT, só a
/// assinatura HMAC no header <c>Stripe-Signature</c>, validada em BillingService/StripeBillingGateway.
/// As demais rotas exigem login, mas ficam de fora do bloqueio do SubscriptionGateMiddleware
/// (senão ninguém conseguiria assinar pra sair do bloqueio).
/// </summary>
[ApiController]
[Route("api/billing")]
public class BillingController : ControllerBase
{
    private readonly BillingService _billingService;

    public BillingController(BillingService billingService) => _billingService = billingService;

    [Authorize]
    [HttpGet("status")]
    public async Task<ActionResult<SubscriptionStatusDto>> GetStatus(CancellationToken ct) =>
        Ok(await _billingService.GetStatusAsync(ct));

    [Authorize]
    [HttpPost("checkout")]
    public async Task<ActionResult<CheckoutSessionDto>> StartCheckout(CancellationToken ct) =>
        Ok(await _billingService.StartCheckoutAsync(ct));

    [Authorize]
    [HttpPost("portal")]
    public async Task<ActionResult<BillingPortalSessionDto>> OpenPortal(CancellationToken ct) =>
        Ok(await _billingService.OpenPortalAsync(ct));

    [AllowAnonymous]
    [HttpPost("webhook")]
    public async Task<IActionResult> ReceiveWebhook(CancellationToken ct)
    {
        string rawBody;
        using (var reader = new StreamReader(Request.Body, System.Text.Encoding.UTF8))
        {
            rawBody = await reader.ReadToEndAsync(ct);
        }

        var signature = Request.Headers["Stripe-Signature"].ToString();
        await _billingService.HandleWebhookAsync(rawBody, signature, ct);

        // Sempre 200: um evento inválido/não tratado não deve fazer o Stripe ficar retentando.
        return Ok();
    }
}
