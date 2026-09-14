using Checkin.Application.DTOs.Checkins;
using Checkin.Application.UseCases.Bookings;
using Checkin.Application.UseCases.Checkins;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Checkin.Api.Controllers;

/// <summary>
/// Ferramenta de teste/simulação: dispara os mesmos casos de uso que o webhook real do Wellhub
/// dispararia (CheckinService/BookingService), sem precisar montar payload/assinatura na mão nem
/// depender do Wellhub disparar um evento de verdade. Pensada pra testar pelo Swagger
/// ("Try it out") durante a integração.
///
/// IMPORTANTE sobre o check-in: isso simula a NOTIFICAÇÃO (o webhook) chegando — o passo
/// seguinte (RegisterWellhubCheckinAsync) ainda chama a Access Control API de verdade
/// (POST /access/v1/validate) contra o Sandbox. O Wellhub só confirma (Approved) um check-in que
/// ELE registrou primeiro do lado dele — chamar este endpoint direto com um gympass_id qualquer
/// sempre resulta em Rejected ("Check-In not found in database"). Para ver Approved de verdade,
/// registre o check-in no Sandbox ANTES (confirmado em 2026-09-10, ver README "Integração
/// Wellhub"): `POST https://apitesting.partners.gympass.com/helper/v1/gyms/{gym_id}/simulate/checkins`
/// com `{"gympass_user_id", "product_id"}` (produtos válidos: GET /api/classes/products/{checkinPointId})
/// — aí sim este endpoint (ou o webhook real) retorna Approved.
/// </summary>
[ApiController]
[Authorize]
[Route("api/simulate/wellhub")]
public class SimulationController : ControllerBase
{
    private readonly CheckinService _checkinService;
    private readonly BookingService _bookingService;

    public SimulationController(CheckinService checkinService, BookingService bookingService)
    {
        _checkinService = checkinService;
        _bookingService = bookingService;
    }

    /// <summary>Simula o evento <c>checkin</c> do Check-in Webhook — mesmo caminho que
    /// WellhubWebhookController.ReceiveEvent segue para <c>event_type: "checkin"</c>. Preencha
    /// firstName/lastName/email/phoneNumber (como o Wellhub manda no payload real) pra testar o
    /// pré-registro automático de aluno quando o gympassId ainda não está cadastrado.</summary>
    public record SimulateCheckinRequest(
        string GympassId,
        string GymExternalId,
        DateTime? OccurredAt = null,
        string? FirstName = null,
        string? LastName = null,
        string? Email = null,
        string? PhoneNumber = null);

    [HttpPost("checkin")]
    public async Task<IActionResult> SimulateCheckin(SimulateCheckinRequest request, CancellationToken ct)
    {
        var occurredAt = request.OccurredAt ?? DateTime.UtcNow;
        var externalCheckinId = $"sim:{request.GympassId}:{request.GymExternalId}:{occurredAt.Ticks}";
        var userInfo = new WellhubUserInfo(request.FirstName, request.LastName, request.Email, request.PhoneNumber);

        var result = await _checkinService.RegisterWellhubCheckinAsync(
            request.GymExternalId, request.GympassId, externalCheckinId, occurredAt, rawPayload: null, userInfo, ct);

        return Ok(result);
    }

    /// <summary>Simula o evento <c>booking-requested</c> — confirma na hora se há vaga no slot, rejeita se não há.</summary>
    public record SimulateBookingRequestedRequest(
        string SlotExternalId,
        string GympassId,
        string ExternalBookingId,
        DateTime? RequestedAt = null);

    [HttpPost("booking-requested")]
    public async Task<IActionResult> SimulateBookingRequested(SimulateBookingRequestedRequest request, CancellationToken ct)
    {
        var result = await _bookingService.HandleBookingRequestedAsync(
            request.SlotExternalId, request.GympassId, request.ExternalBookingId,
            request.RequestedAt ?? DateTime.UtcNow, rawPayload: null, ct);

        return Ok(result);
    }

    /// <summary>Simula os eventos <c>booking-canceled</c> / <c>booking-late-canceled</c> (marque <c>lateCancel</c>).</summary>
    public record SimulateBookingCanceledRequest(string SlotExternalId, string ExternalBookingId, bool LateCancel = false);

    [HttpPost("booking-canceled")]
    public async Task<IActionResult> SimulateBookingCanceled(SimulateBookingCanceledRequest request, CancellationToken ct)
    {
        var result = await _bookingService.HandleBookingCanceledAsync(
            request.SlotExternalId, request.ExternalBookingId, request.LateCancel, ct);

        return result is null
            ? NotFound(new { error = "Nenhuma reserva encontrada com esse externalBookingId (simule um booking-requested antes)." })
            : Ok(result);
    }
}
