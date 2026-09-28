using Checkin.Application.DTOs.Bookings;
using Checkin.Application.UseCases.Bookings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Checkin.Api.Controllers;

/// <summary>Reservas de aula recebidas do Wellhub (via webhook, ver BookingService) — tela
/// "Reservas". Só leitura: confirmar/rejeitar é sempre automático, pela vaga disponível.</summary>
[ApiController]
[Authorize]
[Route("api/bookings")]
public class BookingsController : ControllerBase
{
    private readonly BookingService _bookingService;

    public BookingsController(BookingService bookingService) => _bookingService = bookingService;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BookingListItemDto>>> List(
        [FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate, CancellationToken ct) =>
        Ok(await _bookingService.ListBookingsAsync(startDate, endDate, ct));
}
