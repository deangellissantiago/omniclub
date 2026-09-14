using Checkin.Application.UseCases.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Checkin.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private readonly ReportService _service;

    public ReportsController(ReportService service) => _service = service;

    /// <summary>Relatório de check-ins por aluno.</summary>
    [HttpGet("by-student")]
    public async Task<IActionResult> ByStudent([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate, [FromQuery] string? studentId, CancellationToken ct) =>
        Ok(await _service.ByStudentAsync(startDate, endDate, studentId, ct));

    /// <summary>Relatório de check-ins por escola/unidade (ponto de check-in).</summary>
    [HttpGet("by-school")]
    public async Task<IActionResult> BySchool([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate, [FromQuery] string? checkinPointId, CancellationToken ct) =>
        Ok(await _service.BySchoolAsync(startDate, endDate, checkinPointId, ct));

    /// <summary>Relatório geral de check-ins.</summary>
    [HttpGet("general")]
    public async Task<IActionResult> General([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate, CancellationToken ct) =>
        Ok(await _service.GeneralAsync(startDate, endDate, ct));
}
