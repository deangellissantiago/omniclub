using Checkin.Application.UseCases.Reports;
using Checkin.Domain.Entities;
using Checkin.Domain.Enums;
using Checkin.Tests.Fakes;
using Xunit;

namespace Checkin.Tests;

/// <summary>
/// Cobre os relatórios de "métricas de dia a dia" do roadmap — Fase 1: engajamento (alunos
/// sumidos), horário de pico, ocupação/no-show de aulas e crescimento da base; Fase 2:
/// conciliação de repasse, penetração de app e ranking de unidades com variação.
/// </summary>
public class ReportServiceTests
{
    private const string TenantId = "tenant-1";

    private readonly FakeCheckinRecordRepository _checkins = new();
    private readonly FakeStudentRepository _students = new();
    private readonly FakeCheckinPointRepository _points = new();
    private readonly FakeBookingRepository _bookings = new();

    private ReportService BuildService() => new(
        _checkins, _students, _points, _bookings, new FakeCurrentTenantContext { TenantId = TenantId });

    private Student SeedStudent(string name, DateTime createdAt)
    {
        var student = new Student { TenantId = TenantId, Name = name, CreatedAt = createdAt };
        _students.Students.Add(student);
        return student;
    }

    private void SeedCheckin(string studentId, DateTime occurredAt, string checkinPointId = "point-1", CheckinStatus status = CheckinStatus.Approved) =>
        _checkins.Records.Add(new CheckinRecord { TenantId = TenantId, StudentId = studentId, CheckinPointId = checkinPointId, OccurredAt = occurredAt, Status = status });

    private CheckinPoint SeedPoint(string id, string name, int? pricePerCheckinCents = null)
    {
        var point = new CheckinPoint { Id = id, TenantId = TenantId, App = IntegrationApp.Wellhub, Name = name, PricePerCheckinCents = pricePerCheckinCents };
        _points.Points.Add(point);
        return point;
    }

    [Fact]
    public async Task Engagement_ranks_students_who_never_checked_in_alongside_the_longest_absent()
    {
        var now = DateTime.UtcNow;
        var frequent = SeedStudent("Ana", now.AddDays(-60));
        var goneQuiet = SeedStudent("Bruno", now.AddDays(-60));
        var neverCame = SeedStudent("Carla", now.AddDays(-10));

        SeedCheckin(frequent.Id, now.AddDays(-1));
        SeedCheckin(frequent.Id, now.AddDays(-3));
        SeedCheckin(goneQuiet.Id, now.AddDays(-45)); // fora da janela de 30 dias

        var report = await BuildService().EngagementAsync();

        var ana = report.Students.Single(s => s.StudentId == frequent.Id);
        Assert.Equal(1, ana.DaysSinceLastCheckin); // último check-in foi "ontem"
        Assert.Equal(2, ana.CheckinsLast30Days);
        Assert.True(ana.AvgCheckinsPerWeek > 0);

        var bruno = report.Students.Single(s => s.StudentId == goneQuiet.Id);
        Assert.Equal(45, bruno.DaysSinceLastCheckin);
        Assert.Equal(0, bruno.CheckinsLast30Days);

        var carla = report.Students.Single(s => s.StudentId == neverCame.Id);
        Assert.Null(carla.LastCheckinAt);
        Assert.Null(carla.DaysSinceLastCheckin);

        // Quem nunca veio e quem está sumido há mais tempo aparecem antes de quem é frequente.
        Assert.Equal(new[] { neverCame.Id, goneQuiet.Id, frequent.Id }, report.Students.Select(s => s.StudentId));
    }

    [Fact]
    public async Task Engagement_ignores_inactive_students()
    {
        var inactive = SeedStudent("Dani", DateTime.UtcNow);
        inactive.Active = false;

        var report = await BuildService().EngagementAsync();

        Assert.DoesNotContain(report.Students, s => s.StudentId == inactive.Id);
    }

    [Fact]
    public async Task PeakHours_groups_checkins_by_day_of_week_and_hour()
    {
        // Segunda 18h duas vezes, Terça 7h uma vez.
        var monday = new DateTime(2026, 9, 14, 18, 0, 0, DateTimeKind.Utc); // uma segunda-feira
        SeedCheckin("s1", monday);
        SeedCheckin("s2", monday.AddMinutes(20));
        SeedCheckin("s3", monday.AddDays(1).AddHours(-11)); // terça 7h

        var report = await BuildService().PeakHoursAsync(null, null);

        var mondayCell = report.Cells.Single(c => c.DayOfWeek == DayOfWeek.Monday && c.Hour == 18);
        Assert.Equal(2, mondayCell.TotalCheckins);
        Assert.Contains(report.Cells, c => c.DayOfWeek == DayOfWeek.Tuesday && c.Hour == 7 && c.TotalCheckins == 1);
    }

    private Booking SeedBooking(BookingStatus status, DateTime requestedAt) =>
        new() { TenantId = TenantId, SlotId = "slot-1", GympassId = "member-1", ExternalBookingId = Guid.NewGuid().ToString(), Status = status, RequestedAt = requestedAt };

    [Fact]
    public async Task Attendance_computes_occupancy_and_no_show_rates()
    {
        var now = DateTime.UtcNow;
        _bookings.Bookings.Add(SeedBooking(BookingStatus.Confirmed, now));
        _bookings.Bookings.Add(SeedBooking(BookingStatus.Confirmed, now));
        _bookings.Bookings.Add(SeedBooking(BookingStatus.Confirmed, now));
        _bookings.Bookings.Add(SeedBooking(BookingStatus.Rejected, now));
        _bookings.Bookings.Add(SeedBooking(BookingStatus.LateCanceled, now));

        var report = await BuildService().AttendanceAsync(null, null);

        Assert.Equal(5, report.TotalBookings);
        // 3 confirmadas de 4 decididas (3 confirmed + 1 rejected) = 0.75
        Assert.Equal(0.75, report.OccupancyRate);
        // 1 late-cancel de 4 que chegaram a ser confirmadas (3 confirmed + 1 lateCanceled) = 0.25
        Assert.Equal(0.25, report.NoShowRate);
    }

    [Fact]
    public async Task Attendance_returns_zero_rates_instead_of_dividing_by_zero_when_theres_no_data()
    {
        var report = await BuildService().AttendanceAsync(null, null);

        Assert.Equal(0, report.TotalBookings);
        Assert.Equal(0, report.OccupancyRate);
        Assert.Equal(0, report.NoShowRate);
    }

    [Fact]
    public async Task Growth_groups_new_students_by_week()
    {
        var week1 = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc); // terça
        var week2 = new DateTime(2026, 9, 9, 10, 0, 0, DateTimeKind.Utc); // quarta seguinte
        SeedStudent("A", week1);
        SeedStudent("B", week1.AddDays(1));
        SeedStudent("C", week2);

        var report = await BuildService().GrowthAsync(null, null, "week");

        Assert.Equal("week", report.GroupBy);
        Assert.Equal(2, report.Periods.Count);
        Assert.Equal(2, report.Periods[0].NewStudents);
        Assert.Equal(1, report.Periods[1].NewStudents);
        // O período começa na segunda-feira daquela semana.
        Assert.Equal(DayOfWeek.Monday, report.Periods[0].PeriodStart.DayOfWeek);
    }

    [Fact]
    public async Task Growth_groups_new_students_by_month_when_requested()
    {
        SeedStudent("A", new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc));
        SeedStudent("B", new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc));

        var report = await BuildService().GrowthAsync(null, null, "month");

        Assert.Equal("month", report.GroupBy);
        Assert.Equal(2, report.Periods.Count);
        Assert.Equal(new DateTime(2026, 8, 1), report.Periods[0].PeriodStart);
        Assert.Equal(new DateTime(2026, 9, 1), report.Periods[1].PeriodStart);
    }

    [Fact]
    public async Task Revenue_multiplies_approved_checkins_by_the_configured_price_per_point()
    {
        var now = DateTime.UtcNow;
        SeedPoint("point-1", "Unidade A", pricePerCheckinCents: 150);
        SeedPoint("point-2", "Unidade B", pricePerCheckinCents: null); // sem valor configurado

        SeedCheckin("s1", now, "point-1", CheckinStatus.Approved);
        SeedCheckin("s2", now, "point-1", CheckinStatus.Approved);
        SeedCheckin("s3", now, "point-1", CheckinStatus.Rejected); // não conta — não foi aprovado
        SeedCheckin("s4", now, "point-2", CheckinStatus.Approved);

        var report = await BuildService().RevenueAsync(null, null);

        Assert.Equal(3, report.TotalApprovedCheckins); // 2 aprovados na A + 1 na B (a rejeitada não entra)
        Assert.Equal(300, report.TotalEstimatedRevenueCents); // só a Unidade A tem valor configurado: 2 × 150
        Assert.Equal(1, report.PointsWithoutPriceConfigured);

        var pointA = report.ByPoint.Single(p => p.CheckinPointId == "point-1");
        Assert.Equal(2, pointA.ApprovedCheckins);
        Assert.Equal(300, pointA.EstimatedRevenueCents);

        var pointB = report.ByPoint.Single(p => p.CheckinPointId == "point-2");
        Assert.Equal(1, pointB.ApprovedCheckins);
        Assert.Null(pointB.EstimatedRevenueCents); // nunca estima em cima de valor não informado
    }

    [Fact]
    public async Task Revenue_returns_null_total_when_no_point_has_a_price_configured()
    {
        SeedPoint("point-1", "Unidade A", pricePerCheckinCents: null);
        SeedCheckin("s1", DateTime.UtcNow, "point-1");

        var report = await BuildService().RevenueAsync(null, null);

        Assert.Null(report.TotalEstimatedRevenueCents);
        Assert.Equal(1, report.PointsWithoutPriceConfigured);
    }

    [Fact]
    public async Task AppPenetration_computes_percentage_of_active_students_per_app()
    {
        var withWellhub = SeedStudent("Ana", DateTime.UtcNow);
        withWellhub.WellhubMemberId = "w-1";
        var withTotalPass = SeedStudent("Bruno", DateTime.UtcNow);
        withTotalPass.TotalPassMemberId = "t-1";
        var withNeither = SeedStudent("Carla", DateTime.UtcNow);
        var inactive = SeedStudent("Dani", DateTime.UtcNow);
        inactive.Active = false;
        inactive.WellhubMemberId = "w-2"; // não deve contar — aluno inativo

        var report = await BuildService().AppPenetrationAsync();

        Assert.Equal(3, report.TotalActiveStudents); // Ana, Bruno, Carla — Dani está inativa
        var wellhub = report.Items.Single(i => i.App == "Wellhub");
        Assert.Equal(1, wellhub.ActiveStudents);
        Assert.Equal(0.3333, wellhub.Percent, precision: 4);
    }

    [Fact]
    public async Task SchoolRanking_compares_current_period_against_the_previous_one_of_equal_length()
    {
        SeedPoint("point-1", "Unidade A");
        var periodStart = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = new DateTime(2026, 9, 8, 0, 0, 0, DateTimeKind.Utc); // janela de 7 dias

        // Período anterior (25/08 a 01/09): 2 check-ins.
        SeedCheckin("s1", periodStart.AddDays(-3), "point-1");
        SeedCheckin("s2", periodStart.AddDays(-1), "point-1");
        // Período atual (01/09 a 08/09): 3 check-ins — cresceu 50%.
        SeedCheckin("s1", periodStart.AddDays(1), "point-1");
        SeedCheckin("s2", periodStart.AddDays(2), "point-1");
        SeedCheckin("s3", periodStart.AddDays(3), "point-1");

        var report = await BuildService().SchoolRankingAsync(periodStart, periodEnd);

        var item = report.Items.Single(i => i.CheckinPointId == "point-1");
        Assert.Equal(3, item.TotalCheckins);
        Assert.Equal(2, item.PreviousPeriodCheckins);
        Assert.Equal(0.5, item.ChangePercent);
    }

    [Fact]
    public async Task SchoolRanking_reports_null_change_when_the_previous_period_had_no_checkins()
    {
        SeedPoint("point-1", "Unidade A");
        SeedCheckin("s1", DateTime.UtcNow, "point-1"); // dentro da janela padrão de 30 dias

        var report = await BuildService().SchoolRankingAsync(null, null);

        var item = report.Items.Single(i => i.CheckinPointId == "point-1");
        Assert.Equal(0, item.PreviousPeriodCheckins);
        Assert.Null(item.ChangePercent); // "novo" na UI, não uma variação de 0% ou infinita
    }
}
