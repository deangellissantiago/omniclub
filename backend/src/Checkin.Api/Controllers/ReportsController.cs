using System.Globalization;
using Checkin.Api.Csv;
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

    [HttpGet("by-student/export")]
    public async Task<IActionResult> ByStudentExport([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate, [FromQuery] string? studentId, CancellationToken ct)
    {
        var items = await _service.ByStudentAsync(startDate, endDate, studentId, ct);
        var csv = CsvExporter.Write(items,
            ("Aluno", i => i.StudentName),
            ("Total de check-ins", i => i.TotalCheckins),
            ("Último check-in", i => i.LastCheckinAt));
        return File(csv, "text/csv", "relatorio-por-aluno.csv");
    }

    /// <summary>Relatório de check-ins por escola/unidade (ponto de check-in).</summary>
    [HttpGet("by-school")]
    public async Task<IActionResult> BySchool([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate, [FromQuery] string? checkinPointId, CancellationToken ct) =>
        Ok(await _service.BySchoolAsync(startDate, endDate, checkinPointId, ct));

    [HttpGet("by-school/export")]
    public async Task<IActionResult> BySchoolExport([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate, [FromQuery] string? checkinPointId, CancellationToken ct)
    {
        var items = await _service.BySchoolAsync(startDate, endDate, checkinPointId, ct);
        var csv = CsvExporter.Write(items,
            ("Ponto de check-in", i => i.CheckinPointName),
            ("App", i => i.App),
            ("Total de check-ins", i => i.TotalCheckins),
            ("Último check-in", i => i.LastCheckinAt));
        return File(csv, "text/csv", "relatorio-por-escola.csv");
    }

    /// <summary>Relatório geral de check-ins.</summary>
    [HttpGet("general")]
    public async Task<IActionResult> General([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate, CancellationToken ct) =>
        Ok(await _service.GeneralAsync(startDate, endDate, ct));

    /// <summary>Situação de frequência de cada aluno ativo agora — base do widget "alunos
    /// sumidos" (não é filtrável por período, ver ReportService.EngagementAsync).</summary>
    [HttpGet("engagement")]
    public async Task<IActionResult> Engagement(CancellationToken ct) => Ok(await _service.EngagementAsync(ct));

    [HttpGet("engagement/export")]
    public async Task<IActionResult> EngagementExport(CancellationToken ct)
    {
        var report = await _service.EngagementAsync(ct);
        var csv = CsvExporter.Write(report.Students,
            ("Aluno", i => i.StudentName),
            ("Último check-in", i => i.LastCheckinAt),
            ("Dias sem aparecer", i => i.DaysSinceLastCheckin),
            ("Check-ins (últimos 30 dias)", i => i.CheckinsLast30Days),
            ("Frequência média (por semana)", i => i.AvgCheckinsPerWeek));
        return File(csv, "text/csv", "relatorio-engajamento.csv");
    }

    /// <summary>Heatmap de horário de pico (dia da semana × hora), filtrável por data.</summary>
    [HttpGet("peak-hours")]
    public async Task<IActionResult> PeakHours([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate, CancellationToken ct) =>
        Ok(await _service.PeakHoursAsync(startDate, endDate, ct));

    /// <summary>Ocupação/no-show das aulas reservadas via Booking API, filtrável por data.</summary>
    [HttpGet("attendance")]
    public async Task<IActionResult> Attendance([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate, CancellationToken ct) =>
        Ok(await _service.AttendanceAsync(startDate, endDate, ct));

    /// <summary>Curva de crescimento da base de alunos (novos cadastros por semana/mês), filtrável por data.</summary>
    [HttpGet("growth")]
    public async Task<IActionResult> Growth([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate, [FromQuery] string groupBy = "week", CancellationToken ct = default) =>
        Ok(await _service.GrowthAsync(startDate, endDate, groupBy, ct));

    /// <summary>Conciliação de repasse Wellhub/TotalPass (check-ins aprovados × valor/check-in
    /// configurado por ponto), filtrável por data.</summary>
    [HttpGet("revenue")]
    public async Task<IActionResult> Revenue([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate, CancellationToken ct) =>
        Ok(await _service.RevenueAsync(startDate, endDate, ct));

    [HttpGet("revenue/export")]
    public async Task<IActionResult> RevenueExport([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate, CancellationToken ct)
    {
        var report = await _service.RevenueAsync(startDate, endDate, ct);
        var csv = CsvExporter.Write(report.ByPoint,
            ("Ponto de check-in", i => i.CheckinPointName),
            ("App", i => i.App),
            ("Check-ins aprovados", i => i.ApprovedCheckins),
            ("Valor por check-in (R$)", i => FormatCents(i.PricePerCheckinCents)),
            ("Receita estimada (R$)", i => FormatCents(i.EstimatedRevenueCents)));
        return File(csv, "text/csv", "relatorio-repasse.csv");
    }

    /// <summary>Penetração de cada app de benefício na base de alunos ativos — foto de agora,
    /// não filtrável por período (ver ReportService.AppPenetrationAsync).</summary>
    [HttpGet("app-penetration")]
    public async Task<IActionResult> AppPenetration(CancellationToken ct) => Ok(await _service.AppPenetrationAsync(ct));

    /// <summary>Ranking de unidades com variação período a período (padrão: últimos 30 dias).</summary>
    [HttpGet("school-ranking")]
    public async Task<IActionResult> SchoolRanking([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate, CancellationToken ct) =>
        Ok(await _service.SchoolRankingAsync(startDate, endDate, ct));

    private static string FormatCents(long? cents) =>
        cents.HasValue ? (cents.Value / 100m).ToString("F2", CultureInfo.GetCultureInfo("pt-BR")) : "";
}
