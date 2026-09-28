using Checkin.Application.DTOs.Bookings;
using Checkin.Application.UseCases.Bookings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Checkin.Api.Controllers;

/// <summary>
/// "Configuração de Grade" da Booking API do Wellhub (ver BookingService): cadastro de
/// categorias de aula e dos slots agendados de cada uma — tela "Agenda de aulas". Sem update/
/// delete ainda (o gateway também não tem o DELETE do Wellhub), adiciona conforme a necessidade
/// real de uso aparecer.
/// </summary>
[ApiController]
[Authorize]
[Route("api/classes")]
public class ClassesController : ControllerBase
{
    private readonly BookingService _bookingService;

    public ClassesController(BookingService bookingService) => _bookingService = bookingService;

    /// <summary>Produtos (planos/tipos de acesso) válidos da unidade — um <c>productId</c> daqui
    /// é obrigatório no <see cref="Create"/> abaixo (o Wellhub exige).</summary>
    [HttpGet("products/{checkinPointId}")]
    public async Task<ActionResult<IReadOnlyList<WellhubProductDto>>> ListProducts(string checkinPointId, CancellationToken ct) =>
        Ok(await _bookingService.ListProductsAsync(checkinPointId, ct));

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ClassDto>>> List(CancellationToken ct) =>
        Ok(await _bookingService.ListClassesAsync(ct));

    [HttpPost]
    public async Task<ActionResult<ClassDto>> Create(CreateClassRequest request, CancellationToken ct) =>
        Ok(await _bookingService.CreateClassAsync(request, ct));

    /// <summary>Reenvia ao Wellhub a categoria e os horários futuros que ainda não têm id externo
    /// (ver BookingService.SyncClassAsync).</summary>
    [HttpPost("{classId}/sync")]
    public async Task<ActionResult<ClassDto>> Sync(string classId, CancellationToken ct) =>
        Ok(await _bookingService.SyncClassAsync(classId, ct));

    [HttpGet("{classId}/slots")]
    public async Task<ActionResult<IReadOnlyList<ClassSlotDto>>> ListSlots(string classId, CancellationToken ct) =>
        Ok(await _bookingService.ListSlotsAsync(classId, ct));

    [HttpPost("{classId}/slots")]
    public async Task<ActionResult<ClassSlotDto>> CreateSlot(string classId, CreateSlotBody body, CancellationToken ct) =>
        Ok(await _bookingService.CreateSlotAsync(new CreateSlotRequest(classId, body.StartsAt, body.EndsAt, body.Capacity), ct));

    public record CreateSlotBody(DateTime StartsAt, DateTime EndsAt, int Capacity);
}
