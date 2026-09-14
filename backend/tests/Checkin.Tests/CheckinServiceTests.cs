using Checkin.Application.DTOs.Checkins;
using Checkin.Application.Exceptions;
using Checkin.Application.Ports.Integrations;
using Checkin.Application.UseCases.Checkins;
using Checkin.Domain.Entities;
using Checkin.Domain.Enums;
using Checkin.Tests.Fakes;
using Moq;
using Xunit;

namespace Checkin.Tests;

/// <summary>
/// Cobre o fluxo do Check-in Webhook do Wellhub descrito pelo Technical Sales:
/// webhook notifica -> RegisterWellhubCheckinAsync chama IWellhubGateway.ValidateAccessAsync
/// (POST /access/v1/validate) -> aprova só com resposta positiva.
/// </summary>
public class CheckinServiceTests
{
    private const string TenantId = "tenant-1";
    private const string GymExternalId = "609"; // gym_id de sandbox informado pelo Wellhub
    private const string GympassId = "member-abc-123"; // user.unique_token do payload do webhook

    private readonly FakeCheckinPointRepository _points = new();
    private readonly FakeStudentRepository _students = new();
    private readonly FakeCheckinRecordRepository _records = new();
    private readonly Mock<IWellhubGateway> _gateway = new();

    private CheckinService BuildService() => new(
        _records, _points, _students, _gateway.Object, new FakeCurrentTenantContext { TenantId = TenantId });

    private CheckinPoint SeedCheckinPoint()
    {
        var point = new CheckinPoint { TenantId = TenantId, App = IntegrationApp.Wellhub, ExternalId = GymExternalId, Name = "Sandbox" };
        _points.Points.Add(point);
        return point;
    }

    [Fact]
    public async Task Approves_checkin_when_wellhub_validates_access_successfully()
    {
        SeedCheckinPoint();
        _gateway.SetupGet(g => g.IsConfigured).Returns(true);
        _gateway.Setup(g => g.ValidateAccessAsync(GympassId, null, GymExternalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WellhubValidationResult(GympassId, 609, null, null, DateTime.UtcNow));

        var service = BuildService();
        var dto = await service.RegisterWellhubCheckinAsync(GymExternalId, GympassId, "ext-1", DateTime.UtcNow, "{}");

        Assert.Equal(CheckinStatus.Approved, dto.Status);
        _gateway.Verify(g => g.ValidateAccessAsync(GympassId, null, GymExternalId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Rejects_checkin_when_wellhub_validate_returns_null()
    {
        SeedCheckinPoint();
        _gateway.SetupGet(g => g.IsConfigured).Returns(true);
        _gateway.Setup(g => g.ValidateAccessAsync(GympassId, null, GymExternalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WellhubValidationResult?)null);

        var service = BuildService();
        var dto = await service.RegisterWellhubCheckinAsync(GymExternalId, GympassId, "ext-2", DateTime.UtcNow, "{}");

        Assert.Equal(CheckinStatus.Rejected, dto.Status);
    }

    [Fact]
    public async Task Falls_back_to_local_auto_approval_when_gateway_not_configured()
    {
        SeedCheckinPoint();
        _gateway.SetupGet(g => g.IsConfigured).Returns(false);

        var service = BuildService();
        var dto = await service.RegisterWellhubCheckinAsync(GymExternalId, GympassId, "ext-3", DateTime.UtcNow, "{}");

        Assert.Equal(CheckinStatus.Approved, dto.Status);
        _gateway.Verify(g => g.ValidateAccessAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Is_idempotent_for_repeated_webhook_retries_and_does_not_call_validate_again()
    {
        SeedCheckinPoint();
        _gateway.SetupGet(g => g.IsConfigured).Returns(true);
        _gateway.Setup(g => g.ValidateAccessAsync(GympassId, null, GymExternalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WellhubValidationResult(GympassId, 609, null, null, DateTime.UtcNow));

        var service = BuildService();
        var first = await service.RegisterWellhubCheckinAsync(GymExternalId, GympassId, "ext-retry", DateTime.UtcNow, "{}");
        var second = await service.RegisterWellhubCheckinAsync(GymExternalId, GympassId, "ext-retry", DateTime.UtcNow, "{}");

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, _records.CreateCallCount);
        _gateway.Verify(g => g.ValidateAccessAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Matches_student_by_wellhub_member_id_when_registered()
    {
        var point = SeedCheckinPoint();
        _students.Students.Add(new Student { TenantId = TenantId, Name = "Aluno Teste", WellhubMemberId = GympassId });
        _gateway.SetupGet(g => g.IsConfigured).Returns(true);
        _gateway.Setup(g => g.ValidateAccessAsync(GympassId, null, GymExternalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WellhubValidationResult(GympassId, 609, null, null, DateTime.UtcNow));

        var service = BuildService();
        var dto = await service.RegisterWellhubCheckinAsync(GymExternalId, GympassId, "ext-4", DateTime.UtcNow, "{}");

        Assert.NotNull(dto.StudentId);
        Assert.Equal("Aluno Teste", dto.StudentName);
        Assert.Equal(point.Id, dto.CheckinPointId);
    }

    [Fact]
    public async Task Throws_not_found_when_no_checkin_point_is_registered_for_the_gym_id()
    {
        var service = BuildService();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.RegisterWellhubCheckinAsync("id-desconhecido", GympassId, "ext-5", DateTime.UtcNow, "{}"));
    }

    [Fact]
    public async Task Auto_registers_a_student_when_none_matches_and_wellhub_sends_a_name()
    {
        var point = SeedCheckinPoint();
        _gateway.SetupGet(g => g.IsConfigured).Returns(true);
        _gateway.Setup(g => g.ValidateAccessAsync(GympassId, null, GymExternalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WellhubValidationResult(GympassId, 609, null, null, DateTime.UtcNow));
        var userInfo = new WellhubUserInfo("Mike", "Hightower", "mike@mail.com", "+15165930060");

        var service = BuildService();
        var dto = await service.RegisterWellhubCheckinAsync(GymExternalId, GympassId, "ext-6", DateTime.UtcNow, "{}", userInfo);

        Assert.Equal("Mike Hightower", dto.StudentName);
        Assert.NotNull(dto.StudentId);

        var created = Assert.Single(_students.Students);
        Assert.Equal(TenantId, created.TenantId);
        Assert.Equal("Mike Hightower", created.Name);
        Assert.Equal("mike@mail.com", created.Email);
        Assert.Equal("+15165930060", created.Phone);
        Assert.Equal(GympassId, created.WellhubMemberId);
        Assert.Equal(point.TenantId, created.TenantId);
    }

    [Fact]
    public async Task Does_not_auto_register_a_student_when_no_name_is_provided()
    {
        SeedCheckinPoint();
        _gateway.SetupGet(g => g.IsConfigured).Returns(true);
        _gateway.Setup(g => g.ValidateAccessAsync(GympassId, null, GymExternalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WellhubValidationResult(GympassId, 609, null, null, DateTime.UtcNow));

        var service = BuildService();
        var dto = await service.RegisterWellhubCheckinAsync(GymExternalId, GympassId, "ext-7", DateTime.UtcNow, "{}"); // sem userInfo

        Assert.Null(dto.StudentId);
        Assert.Null(dto.StudentName);
        Assert.Empty(_students.Students);
    }

    [Fact]
    public async Task Does_not_create_a_duplicate_student_when_one_already_matches()
    {
        SeedCheckinPoint();
        _students.Students.Add(new Student { TenantId = TenantId, Name = "Aluno Já Cadastrado", WellhubMemberId = GympassId });
        _gateway.SetupGet(g => g.IsConfigured).Returns(true);
        _gateway.Setup(g => g.ValidateAccessAsync(GympassId, null, GymExternalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WellhubValidationResult(GympassId, 609, null, null, DateTime.UtcNow));
        var userInfo = new WellhubUserInfo("Outro", "Nome", "outro@mail.com", null);

        var service = BuildService();
        var dto = await service.RegisterWellhubCheckinAsync(GymExternalId, GympassId, "ext-8", DateTime.UtcNow, "{}", userInfo);

        Assert.Equal("Aluno Já Cadastrado", dto.StudentName); // mantém o cadastro existente, não sobrescreve
        Assert.Single(_students.Students);
    }
}
