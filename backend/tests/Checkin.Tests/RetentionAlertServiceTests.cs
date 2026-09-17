using Checkin.Application.UseCases.Retention;
using Checkin.Domain.Entities;
using Checkin.Domain.Enums;
using Checkin.Tests.Fakes;
using Xunit;

namespace Checkin.Tests;

/// <summary>
/// Cobre a régua de relacionamento automática (Fase 3 do roadmap): alerta de inatividade,
/// aniversário e queda de movimento por ponto — sempre via WhatsApp (template), com os
/// respectivos cooldowns pra não spammar o mesmo aluno/ponto todo dia.
/// </summary>
public class RetentionAlertServiceTests
{
    private const string TenantId = "tenant-1";

    private readonly FakeCheckinRecordRepository _checkins = new();
    private readonly FakeStudentRepository _students = new();
    private readonly FakeCheckinPointRepository _points = new();
    private readonly FakeTenantRepository _tenants = new();
    private readonly FakeWhatsAppSender _whatsApp = new();

    private RetentionAlertService BuildService() => new(
        _checkins, _students, _points, _tenants, _whatsApp,
        Microsoft.Extensions.Logging.Abstractions.NullLogger<RetentionAlertService>.Instance);

    private Tenant SeedTenant(SubscriptionStatus status = SubscriptionStatus.Active, string? alertsPhone = null)
    {
        var tenant = new Tenant { Id = TenantId, Name = "Escola de Tênis", SubscriptionStatus = status, AlertsWhatsAppPhone = alertsPhone };
        _tenants.Tenants.Add(tenant);
        return tenant;
    }

    private Student SeedStudent(string name, string? phone = "5531999999999", DateTime? birthDate = null)
    {
        var student = new Student { TenantId = TenantId, Name = name, Phone = phone, BirthDate = birthDate };
        _students.Students.Add(student);
        return student;
    }

    private void SeedCheckin(string studentId, DateTime occurredAt, string checkinPointId = "point-1") =>
        _checkins.Records.Add(new CheckinRecord { TenantId = TenantId, StudentId = studentId, CheckinPointId = checkinPointId, OccurredAt = occurredAt, Status = CheckinStatus.Approved });

    private CheckinPoint SeedPoint(string id, string name = "Unidade A")
    {
        var point = new CheckinPoint { Id = id, TenantId = TenantId, App = IntegrationApp.Wellhub, Name = name };
        _points.Points.Add(point);
        return point;
    }

    // ---------- Inatividade ----------

    [Fact]
    public async Task Inactivity_alerts_a_student_who_stopped_coming_14_or_more_days_ago()
    {
        var tenant = SeedTenant();
        var student = SeedStudent("Ana");
        SeedCheckin(student.Id, DateTime.UtcNow.AddDays(-20));

        var sent = await BuildService().SendInactivityAlertsAsync(tenant);

        Assert.Equal(1, sent);
        var message = Assert.Single(_whatsApp.Sent);
        Assert.Equal(RetentionAlertService.InactivityTemplateName, message.TemplateName);
        Assert.Equal(student.Phone, message.ToPhone);
        Assert.Equal(new[] { "Ana", "Escola de Tênis" }, message.Parameters);
        Assert.NotNull(student.LastInactivityAlertSentAt);
    }

    [Fact]
    public async Task Inactivity_does_not_alert_a_student_below_the_threshold()
    {
        var tenant = SeedTenant();
        var student = SeedStudent("Ana");
        SeedCheckin(student.Id, DateTime.UtcNow.AddDays(-5));

        var sent = await BuildService().SendInactivityAlertsAsync(tenant);

        Assert.Equal(0, sent);
    }

    [Fact]
    public async Task Inactivity_ignores_a_student_who_never_checked_in()
    {
        var tenant = SeedTenant();
        SeedStudent("Nunca veio");

        var sent = await BuildService().SendInactivityAlertsAsync(tenant);

        Assert.Equal(0, sent);
    }

    [Fact]
    public async Task Inactivity_skips_a_student_without_a_phone_number()
    {
        var tenant = SeedTenant();
        var student = SeedStudent("Ana", phone: null);
        SeedCheckin(student.Id, DateTime.UtcNow.AddDays(-20));

        var sent = await BuildService().SendInactivityAlertsAsync(tenant);

        Assert.Equal(0, sent);
    }

    [Fact]
    public async Task Inactivity_respects_the_cooldown_between_alerts_to_the_same_student()
    {
        var tenant = SeedTenant();
        var student = SeedStudent("Ana");
        SeedCheckin(student.Id, DateTime.UtcNow.AddDays(-20));
        student.LastInactivityAlertSentAt = DateTime.UtcNow.AddDays(-3); // mandou há 3 dias, cooldown é 14

        var sent = await BuildService().SendInactivityAlertsAsync(tenant);

        Assert.Equal(0, sent);
    }

    [Fact]
    public async Task Inactivity_alerts_again_once_the_cooldown_has_passed()
    {
        var tenant = SeedTenant();
        var student = SeedStudent("Ana");
        SeedCheckin(student.Id, DateTime.UtcNow.AddDays(-20));
        student.LastInactivityAlertSentAt = DateTime.UtcNow.AddDays(-15);

        var sent = await BuildService().SendInactivityAlertsAsync(tenant);

        Assert.Equal(1, sent);
    }

    // ---------- Aniversário ----------

    [Fact]
    public async Task Birthday_alerts_a_student_whose_birthday_is_today()
    {
        var tenant = SeedTenant();
        var today = DateTime.UtcNow;
        var student = SeedStudent("Ana", birthDate: new DateTime(1990, today.Month, today.Day));

        var sent = await BuildService().SendBirthdayAlertsAsync(tenant);

        Assert.Equal(1, sent);
        var message = Assert.Single(_whatsApp.Sent);
        Assert.Equal(RetentionAlertService.BirthdayTemplateName, message.TemplateName);
        Assert.Equal(today.Year, student.LastBirthdayAlertSentYear);
    }

    [Fact]
    public async Task Birthday_does_not_alert_on_a_different_day()
    {
        var tenant = SeedTenant();
        var notToday = DateTime.UtcNow.AddDays(10);
        SeedStudent("Ana", birthDate: new DateTime(1990, notToday.Month, notToday.Day));

        var sent = await BuildService().SendBirthdayAlertsAsync(tenant);

        Assert.Equal(0, sent);
    }

    [Fact]
    public async Task Birthday_does_not_alert_twice_in_the_same_year()
    {
        var tenant = SeedTenant();
        var today = DateTime.UtcNow;
        var student = SeedStudent("Ana", birthDate: new DateTime(1990, today.Month, today.Day));
        student.LastBirthdayAlertSentYear = today.Year;

        var sent = await BuildService().SendBirthdayAlertsAsync(tenant);

        Assert.Equal(0, sent);
    }

    [Fact]
    public async Task Birthday_ignores_students_without_a_birth_date()
    {
        var tenant = SeedTenant();
        SeedStudent("Ana", birthDate: null);

        var sent = await BuildService().SendBirthdayAlertsAsync(tenant);

        Assert.Equal(0, sent);
    }

    // ---------- Queda de movimento ----------

    [Fact]
    public async Task PointDrop_alerts_when_checkins_fall_30_percent_or_more_vs_the_previous_week()
    {
        var tenant = SeedTenant(alertsPhone: "5531988888888");
        var point = SeedPoint("point-1");
        var now = DateTime.UtcNow;

        // Semana anterior (14-7 dias atrás): 10 check-ins.
        for (var i = 0; i < 10; i++) SeedCheckin($"s{i}", now.AddDays(-10), point.Id);
        // Semana atual (últimos 7 dias): 6 check-ins -> caiu 40%.
        for (var i = 0; i < 6; i++) SeedCheckin($"c{i}", now.AddDays(-2), point.Id);

        var sent = await BuildService().SendPointDropAlertsAsync(tenant);

        Assert.Equal(1, sent);
        var message = Assert.Single(_whatsApp.Sent);
        Assert.Equal(RetentionAlertService.PointDropTemplateName, message.TemplateName);
        Assert.Equal(tenant.AlertsWhatsAppPhone, message.ToPhone);
        Assert.Equal("40", message.Parameters[1]);
        Assert.NotNull(point.LastDropAlertSentAt);
    }

    [Fact]
    public async Task PointDrop_ignores_points_with_too_little_volume_to_mean_anything()
    {
        var tenant = SeedTenant(alertsPhone: "5531988888888");
        var point = SeedPoint("point-1");
        var now = DateTime.UtcNow;

        // Só 3 check-ins na semana anterior — abaixo do mínimo de 5, mesmo caindo pra 0 não alerta.
        for (var i = 0; i < 3; i++) SeedCheckin($"s{i}", now.AddDays(-10), point.Id);

        var sent = await BuildService().SendPointDropAlertsAsync(tenant);

        Assert.Equal(0, sent);
    }

    [Fact]
    public async Task PointDrop_does_not_alert_when_the_tenant_has_no_alerts_phone_configured()
    {
        var tenant = SeedTenant(alertsPhone: null);
        var point = SeedPoint("point-1");
        var now = DateTime.UtcNow;
        for (var i = 0; i < 10; i++) SeedCheckin($"s{i}", now.AddDays(-10), point.Id);

        var sent = await BuildService().SendPointDropAlertsAsync(tenant);

        Assert.Equal(0, sent);
    }

    [Fact]
    public async Task PointDrop_respects_the_cooldown_between_alerts_for_the_same_point()
    {
        var tenant = SeedTenant(alertsPhone: "5531988888888");
        var point = SeedPoint("point-1");
        point.LastDropAlertSentAt = DateTime.UtcNow.AddDays(-2); // cooldown é 7 dias
        var now = DateTime.UtcNow;
        for (var i = 0; i < 10; i++) SeedCheckin($"s{i}", now.AddDays(-10), point.Id);

        var sent = await BuildService().SendPointDropAlertsAsync(tenant);

        Assert.Equal(0, sent);
    }

    [Fact]
    public async Task PointDrop_does_not_alert_when_checkins_grew_instead_of_dropping()
    {
        var tenant = SeedTenant(alertsPhone: "5531988888888");
        var point = SeedPoint("point-1");
        var now = DateTime.UtcNow;
        for (var i = 0; i < 5; i++) SeedCheckin($"s{i}", now.AddDays(-10), point.Id);
        for (var i = 0; i < 8; i++) SeedCheckin($"c{i}", now.AddDays(-2), point.Id);

        var sent = await BuildService().SendPointDropAlertsAsync(tenant);

        Assert.Equal(0, sent);
    }

    // ---------- Orquestração ----------

    [Fact]
    public async Task RunDailyChecksAsync_does_nothing_when_whatsapp_is_not_configured()
    {
        _whatsApp.IsConfigured = false;
        var tenant = SeedTenant();
        var student = SeedStudent("Ana");
        SeedCheckin(student.Id, DateTime.UtcNow.AddDays(-20));

        await BuildService().RunDailyChecksAsync();

        Assert.Empty(_whatsApp.Sent);
    }

    [Fact]
    public async Task RunDailyChecksAsync_skips_tenants_that_never_activated_or_already_canceled()
    {
        SeedTenant(status: SubscriptionStatus.Inactive);
        var student = SeedStudent("Ana");
        SeedCheckin(student.Id, DateTime.UtcNow.AddDays(-20));

        await BuildService().RunDailyChecksAsync();

        Assert.Empty(_whatsApp.Sent);
    }

    [Fact]
    public async Task RunDailyChecksAsync_processes_active_and_past_due_tenants()
    {
        SeedTenant(status: SubscriptionStatus.PastDue);
        var student = SeedStudent("Ana");
        SeedCheckin(student.Id, DateTime.UtcNow.AddDays(-20));

        await BuildService().RunDailyChecksAsync();

        Assert.Single(_whatsApp.Sent);
    }
}
