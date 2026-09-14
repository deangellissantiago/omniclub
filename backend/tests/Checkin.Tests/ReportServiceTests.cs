using Checkin.Application.UseCases.Reports;
using Checkin.Domain.Entities;
using Checkin.Domain.Enums;
using Checkin.Tests.Fakes;
using Xunit;

namespace Checkin.Tests;

/// <summary>
/// Cobre os relatórios de "métricas de dia a dia" da Fase 1 do roadmap: engajamento (alunos
/// sumidos), horário de pico, ocupação/no-show de aulas e crescimento da base.
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

    private void SeedCheckin(string studentId, DateTime occurredAt) =>
        _checkins.Records.Add(new CheckinRecord { TenantId = TenantId, StudentId = studentId, CheckinPointId = "point-1", OccurredAt = occurredAt });

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
}
