using Checkin.Application.UseCases.Checkins;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Checkin.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/checkins")]
public class CheckinsController : ControllerBase
{
    private readonly CheckinService _service;

    public CheckinsController(CheckinService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate,
        [FromQuery] string? studentId, [FromQuery] string? checkinPointId,
        CancellationToken ct) =>
        Ok(await _service.ListAsync(startDate, endDate, studentId, checkinPointId, ct));
}
