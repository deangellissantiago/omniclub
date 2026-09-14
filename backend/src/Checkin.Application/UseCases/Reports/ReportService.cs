using Checkin.Application.DTOs.Dashboard;
using Checkin.Application.DTOs.Reports;
using Checkin.Application.Ports;
using Checkin.Application.Ports.Repositories;
using Checkin.Domain.Enums;

namespace Checkin.Application.UseCases.Reports;

public class ReportService
{
    private readonly ICheckinRecordRepository _checkinRepository;
    private readonly IStudentRepository _studentRepository;
    private readonly ICheckinPointRepository _checkinPointRepository;
    private readonly IBookingRepository _bookingRepository;
    private readonly ICurrentTenantContext _tenantContext;

    public ReportService(
        ICheckinRecordRepository checkinRepository,
        IStudentRepository studentRepository,
        ICheckinPointRepository checkinPointRepository,
        IBookingRepository bookingRepository,
        ICurrentTenantContext tenantContext)
    {
        _checkinRepository = checkinRepository;
        _studentRepository = studentRepository;
        _checkinPointRepository = checkinPointRepository;
        _bookingRepository = bookingRepository;
        _tenantContext = tenantContext;
    }

    /// <summary>Relatório de check-ins por aluno (todos ou de um aluno específico), filtrável por data.</summary>
    public async Task<IReadOnlyList<StudentReportItemDto>> ByStudentAsync(DateTime? start, DateTime? end, string? studentId, CancellationToken ct = default)
    {
        var records = await _checkinRepository.ListAsync(_tenantContext.TenantId, start, end, studentId, null, ct);
        var students = await _studentRepository.ListAsync(_tenantContext.TenantId, ct);

        return records
            .Where(r => r.StudentId is not null)
            .GroupBy(r => r.StudentId!)
            .Select(g => new StudentReportItemDto(
                g.Key,
                students.FirstOrDefault(s => s.Id == g.Key)?.Name ?? "Aluno não identificado",
                g.LongCount(),
                g.Max(r => (DateTime?)r.OccurredAt)))
            .OrderByDescending(x => x.TotalCheckins)
            .ToList();
    }

    /// <summary>Relatório de check-ins por escola/ponto de check-in, filtrável por data.</summary>
    public async Task<IReadOnlyList<SchoolReportItemDto>> BySchoolAsync(DateTime? start, DateTime? end, string? checkinPointId, CancellationToken ct = default)
    {
        var records = await _checkinRepository.ListAsync(_tenantContext.TenantId, start, end, null, checkinPointId, ct);
        var points = await _checkinPointRepository.ListAsync(_tenantContext.TenantId, ct);

        return records
            .GroupBy(r => r.CheckinPointId)
            .Select(g =>
            {
                var point = points.FirstOrDefault(p => p.Id == g.Key);
                return new SchoolReportItemDto(g.Key, point?.Name ?? "Ponto removido", point?.App.ToString() ?? "-", g.LongCount(), g.Max(r => (DateTime?)r.OccurredAt));
            })
            .OrderByDescending(x => x.TotalCheckins)
            .ToList();
    }

    /// <summary>Relatório geral de check-ins (totais, por app, top alunos e por ponto), filtrável por data.</summary>
    public async Task<GeneralReportDto> GeneralAsync(DateTime? start, DateTime? end, CancellationToken ct = default)
    {
        var total = await _checkinRepository.CountAsync(_tenantContext.TenantId, start, end, ct);

        var byApp = new List<AppCountDto>();
        foreach (var app in Enum.GetValues<IntegrationApp>())
        {
            byApp.Add(new AppCountDto(app.ToString(), await _checkinRepository.CountByAppAsync(_tenantContext.TenantId, app, start, end, ct)));
        }

        var topStudents = (await ByStudentAsync(start, end, null, ct)).Take(10).ToList();
        var byCheckinPoint = await BySchoolAsync(start, end, null, ct);

        return new GeneralReportDto(total, byApp, topStudents, byCheckinPoint);
    }

    /// <summary>Situação de frequência de cada aluno ativo "agora" — não é filtrável por período,
    /// sempre relativo ao instante da chamada: quando foi o último check-in de cada um e quantas
    /// vezes apareceu nos últimos 30 dias. Base do widget "alunos sumidos" (ver roadmap Fase 1).</summary>
    public async Task<EngagementReportDto> EngagementAsync(CancellationToken ct = default)
    {
        var tenantId = _tenantContext.TenantId;
        var now = DateTime.UtcNow;

        var students = await _studentRepository.ListAsync(tenantId, ct);
        var lastCheckins = await _checkinRepository.GetLastCheckinAtByStudentAsync(tenantId, ct);

        var last30DaysRecords = await _checkinRepository.ListAsync(tenantId, now.AddDays(-30), now, ct: ct);
        var last30DaysByStudent = last30DaysRecords
            .Where(r => r.StudentId is not null)
            .GroupBy(r => r.StudentId!)
            .ToDictionary(g => g.Key, g => g.LongCount());

        var items = students
            .Where(s => s.Active)
            .Select(s =>
            {
                var hasCheckedIn = lastCheckins.TryGetValue(s.Id, out var lastAt);
                last30DaysByStudent.TryGetValue(s.Id, out var last30Days);

                return new StudentEngagementDto(
                    s.Id,
                    s.Name,
                    hasCheckedIn ? lastAt : null,
                    hasCheckedIn ? (int)(now.Date - lastAt.Date).TotalDays : null,
                    last30Days,
                    Math.Round(last30Days / 30.0 * 7, 1));
            })
            // Quem nunca veio (sem check-in algum) aparece primeiro, junto dos mais "sumidos" —
            // é o mesmo alerta de retenção que um aluno que veio uma vez há 90 dias.
            .OrderByDescending(x => x.DaysSinceLastCheckin ?? int.MaxValue)
            .ThenBy(x => x.StudentName)
            .ToList();

        return new EngagementReportDto(items, now);
    }

    /// <summary>Heatmap de horário de pico (dia da semana × hora), filtrável por data.</summary>
    public async Task<PeakHoursReportDto> PeakHoursAsync(DateTime? start, DateTime? end, CancellationToken ct = default)
    {
        var records = await _checkinRepository.ListAsync(_tenantContext.TenantId, start, end, ct: ct);

        var cells = records
            .GroupBy(r => (r.OccurredAt.DayOfWeek, r.OccurredAt.Hour))
            .Select(g => new PeakHourCellDto(g.Key.DayOfWeek, g.Key.Hour, g.LongCount()))
            .OrderBy(c => c.DayOfWeek)
            .ThenBy(c => c.Hour)
            .ToList();

        return new PeakHoursReportDto(cells);
    }

    /// <summary>Ocupação/no-show das aulas reservadas via Booking API, filtrável por data.</summary>
    public async Task<AttendanceReportDto> AttendanceAsync(DateTime? start, DateTime? end, CancellationToken ct = default)
    {
        var bookings = await _bookingRepository.ListAsync(_tenantContext.TenantId, start, end, ct);

        var byStatus = bookings
            .GroupBy(b => b.Status)
            .Select(g => new BookingStatusCountDto(g.Key, g.LongCount()))
            .OrderBy(x => x.Status)
            .ToList();

        long CountOf(BookingStatus status) => byStatus.FirstOrDefault(x => x.Status == status)?.Total ?? 0;
        var confirmed = CountOf(BookingStatus.Confirmed);
        var rejected = CountOf(BookingStatus.Rejected);
        var lateCanceled = CountOf(BookingStatus.LateCanceled);

        var decided = confirmed + rejected;
        var occupancyRate = decided == 0 ? 0 : Math.Round((double)confirmed / decided, 4);

        var everConfirmed = confirmed + lateCanceled;
        var noShowRate = everConfirmed == 0 ? 0 : Math.Round((double)lateCanceled / everConfirmed, 4);

        return new AttendanceReportDto(bookings.Count, byStatus, occupancyRate, noShowRate);
    }

    /// <summary>Curva de crescimento da base de alunos (novos cadastros por semana ou mês),
    /// filtrável por data.</summary>
    public async Task<GrowthReportDto> GrowthAsync(DateTime? start, DateTime? end, string groupBy = "week", CancellationToken ct = default)
    {
        var byMonth = string.Equals(groupBy, "month", StringComparison.OrdinalIgnoreCase);
        var students = await _studentRepository.ListAsync(_tenantContext.TenantId, ct);

        var periods = students
            .Where(s => (!start.HasValue || s.CreatedAt >= start.Value) && (!end.HasValue || s.CreatedAt <= end.Value))
            .GroupBy(s => byMonth ? StartOfMonth(s.CreatedAt) : StartOfWeek(s.CreatedAt))
            .Select(g => new GrowthPeriodDto(g.Key, g.LongCount()))
            .OrderBy(p => p.PeriodStart)
            .ToList();

        return new GrowthReportDto(byMonth ? "month" : "week", periods);
    }

    private static DateTime StartOfMonth(DateTime date) => new(date.Year, date.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    private static DateTime StartOfWeek(DateTime date)
    {
        var diff = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return DateTime.SpecifyKind(date.Date.AddDays(-diff), DateTimeKind.Utc);
    }

    /// <summary>Conciliação de repasse: check-ins Approved × valor/check-in configurado por
    /// ponto, filtrável por data. Diferencial que só o OmniClub calcula — nenhum sistema de
    /// gestão de academia genérico tem acesso ao dado de check-in do app de benefício.</summary>
    public async Task<RevenueReportDto> RevenueAsync(DateTime? start, DateTime? end, CancellationToken ct = default)
    {
        var tenantId = _tenantContext.TenantId;
        var records = await _checkinRepository.ListAsync(tenantId, start, end, ct: ct);
        var points = await _checkinPointRepository.ListAsync(tenantId, ct);

        var approvedByPoint = records
            .Where(r => r.Status == CheckinStatus.Approved)
            .GroupBy(r => r.CheckinPointId)
            .ToDictionary(g => g.Key, g => g.LongCount());

        var items = points
            .Select(p =>
            {
                approvedByPoint.TryGetValue(p.Id, out var approved);
                long? revenue = p.PricePerCheckinCents.HasValue ? approved * p.PricePerCheckinCents.Value : null;
                return new RevenuePerPointDto(p.Id, p.Name, p.App.ToString(), approved, p.PricePerCheckinCents, revenue);
            })
            .OrderByDescending(x => x.EstimatedRevenueCents ?? -1) // configurados primeiro, ordenados por valor; não configurados no fim
            .ThenByDescending(x => x.ApprovedCheckins)
            .ToList();

        var configured = items.Where(i => i.EstimatedRevenueCents.HasValue).ToList();

        return new RevenueReportDto(
            items.Sum(i => i.ApprovedCheckins),
            configured.Count == 0 ? null : configured.Sum(i => i.EstimatedRevenueCents!.Value),
            items,
            items.Count(i => !i.PricePerCheckinCents.HasValue));
    }

    /// <summary>Quanto da base de alunos ativos cada app de benefício representa — foto de
    /// agora, não filtrável por período (mesma filosofia do relatório de Engajamento).</summary>
    public async Task<AppPenetrationReportDto> AppPenetrationAsync(CancellationToken ct = default)
    {
        var activeStudents = (await _studentRepository.ListAsync(_tenantContext.TenantId, ct))
            .Where(s => s.Active)
            .ToList();

        var items = Enum.GetValues<IntegrationApp>()
            .Select(app =>
            {
                var withApp = activeStudents.Count(s => app switch
                {
                    IntegrationApp.Wellhub => !string.IsNullOrEmpty(s.WellhubMemberId),
                    IntegrationApp.TotalPass => !string.IsNullOrEmpty(s.TotalPassMemberId),
                    _ => false,
                });
                var percent = activeStudents.Count == 0 ? 0 : Math.Round((double)withApp / activeStudents.Count, 4);
                return new AppPenetrationItemDto(app.ToString(), withApp, percent);
            })
            .ToList();

        return new AppPenetrationReportDto(activeStudents.Count, items);
    }

    /// <summary>Ranking de unidades com variação período a período — compara o período pedido
    /// (padrão: últimos 30 dias) com o período anterior de mesma duração, logo antes dele.</summary>
    public async Task<SchoolRankingReportDto> SchoolRankingAsync(DateTime? start, DateTime? end, CancellationToken ct = default)
    {
        var periodEnd = end ?? DateTime.UtcNow;
        var periodStart = start ?? periodEnd.AddDays(-30);
        var duration = periodEnd - periodStart;
        var previousStart = periodStart - duration;

        var current = await BySchoolAsync(periodStart, periodEnd, null, ct);
        var previousByPoint = (await BySchoolAsync(previousStart, periodStart, null, ct))
            .ToDictionary(p => p.CheckinPointId, p => p.TotalCheckins);

        var items = current
            .Select(c =>
            {
                previousByPoint.TryGetValue(c.CheckinPointId, out var previousTotal);
                double? changePercent = previousTotal == 0 ? null : Math.Round((double)(c.TotalCheckins - previousTotal) / previousTotal, 4);
                return new SchoolRankingItemDto(c.CheckinPointId, c.CheckinPointName, c.App, c.TotalCheckins, previousTotal, changePercent);
            })
            .OrderByDescending(x => x.TotalCheckins)
            .ToList();

        return new SchoolRankingReportDto(periodStart, periodEnd, items);
    }
}
