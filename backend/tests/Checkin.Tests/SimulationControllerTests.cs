using Checkin.Api.Controllers;
using Checkin.Application.DTOs.Bookings;
using Checkin.Application.DTOs.Checkins;
using Checkin.Application.Exceptions;
using Checkin.Application.Ports.Integrations;
using Checkin.Application.UseCases.Bookings;
using Checkin.Application.UseCases.Checkins;
using Checkin.Domain.Entities;
using Checkin.Domain.Enums;
using Checkin.Tests.Fakes;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Checkin.Tests;

/// <summary>
/// Cobre o grupo "Simulation" do Swagger (ver README, "Testes") — a ferramenta que o
/// Deangellis vai usar pra testar/simular check-ins e reservas sem depender do Wellhub disparar
/// um evento de verdade. Mesma bateria de testes rodada manualmente por curl em 2026-09-10
/// (caminho feliz, gym desconhecido, matching de aluno, idempotência, corpo vazio/malformado —
/// os dois últimos são cobertos pela validação automática do [ApiController], não por teste
/// aqui) formalizada como teste automatizado.
/// </summary>
public class SimulationControllerTests
{
    private const string TenantId = "tenant-1";
    private const string GymExternalId = "609";
    private const string GympassId = "sim-user-001";

    private readonly FakeCheckinPointRepository _points = new();
    private readonly FakeStudentRepository _students = new();
    private readonly FakeCheckinRecordRepository _records = new();
    private readonly FakeClassRepository _classes = new();
    private readonly FakeClassSlotRepository _slots = new();
    private readonly FakeBookingRepository _bookings = new();
    private readonly Mock<IWellhubGateway> _checkinGateway = new();
    private readonly Mock<IWellhubBookingGateway> _bookingGateway = new();

    private SimulationController BuildController()
    {
        var tenantContext = new FakeCurrentTenantContext { TenantId = TenantId };
        var checkinService = new CheckinService(_records, _points, _students, _checkinGateway.Object, tenantContext);
        var bookingService = new BookingService(_classes, _slots, _bookings, _students, _points, _bookingGateway.Object, tenantContext);
        return new SimulationController(checkinService, bookingService);
    }

    private void SeedCheckinPoint() =>
        _points.Points.Add(new CheckinPoint { TenantId = TenantId, App = IntegrationApp.Wellhub, ExternalId = GymExternalId, Name = "Sandbox" });

    [Fact]
    public async Task SimulateCheckin_returns_ok_with_the_checkin_service_result()
    {
        SeedCheckinPoint();
        _checkinGateway.SetupGet(g => g.IsConfigured).Returns(true);
        _checkinGateway.Setup(g => g.ValidateAccessAsync(GympassId, null, GymExternalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WellhubValidationResult?)null); // reflete o Sandbox real: sem check-in de verdade, sempre Rejected

        var controller = BuildController();
        var result = await controller.SimulateCheckin(new SimulationController.SimulateCheckinRequest(GympassId, GymExternalId), default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<CheckinDto>(ok.Value);
        Assert.Equal(CheckinStatus.Rejected, dto.Status);
    }

    [Fact]
    public async Task SimulateCheckin_matches_an_existing_student_by_wellhub_member_id()
    {
        SeedCheckinPoint();
        _students.Students.Add(new Student { TenantId = TenantId, Name = "Aluno Simulação", WellhubMemberId = GympassId });
        _checkinGateway.SetupGet(g => g.IsConfigured).Returns(false); // fallback local: aprova direto

        var controller = BuildController();
        var result = await controller.SimulateCheckin(new SimulationController.SimulateCheckinRequest(GympassId, GymExternalId), default);

        var dto = Assert.IsType<CheckinDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("Aluno Simulação", dto.StudentName);
        Assert.Equal(CheckinStatus.Approved, dto.Status);
    }

    [Fact]
    public async Task SimulateCheckin_is_idempotent_when_occurredAt_is_explicit()
    {
        SeedCheckinPoint();
        _checkinGateway.SetupGet(g => g.IsConfigured).Returns(false);
        var controller = BuildController();
        var occurredAt = new DateTime(2026, 9, 10, 10, 0, 0, DateTimeKind.Utc);

        var first = await controller.SimulateCheckin(new SimulationController.SimulateCheckinRequest(GympassId, GymExternalId, occurredAt), default);
        var second = await controller.SimulateCheckin(new SimulationController.SimulateCheckinRequest(GympassId, GymExternalId, occurredAt), default);

        var firstDto = Assert.IsType<CheckinDto>(Assert.IsType<OkObjectResult>(first).Value);
        var secondDto = Assert.IsType<CheckinDto>(Assert.IsType<OkObjectResult>(second).Value);
        Assert.Equal(firstDto.Id, secondDto.Id);
        Assert.Equal(1, _records.CreateCallCount);
    }

    [Fact]
    public async Task SimulateCheckin_without_occurredAt_creates_a_distinct_record_each_call()
    {
        SeedCheckinPoint();
        _checkinGateway.SetupGet(g => g.IsConfigured).Returns(false);
        var controller = BuildController();

        var first = await controller.SimulateCheckin(new SimulationController.SimulateCheckinRequest(GympassId, GymExternalId), default);
        var second = await controller.SimulateCheckin(new SimulationController.SimulateCheckinRequest(GympassId, GymExternalId), default);

        var firstDto = Assert.IsType<CheckinDto>(Assert.IsType<OkObjectResult>(first).Value);
        var secondDto = Assert.IsType<CheckinDto>(Assert.IsType<OkObjectResult>(second).Value);
        Assert.NotEqual(firstDto.Id, secondDto.Id);
    }

    [Fact]
    public async Task SimulateCheckin_throws_not_found_for_an_unknown_gym_id()
    {
        var controller = BuildController();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            controller.SimulateCheckin(new SimulationController.SimulateCheckinRequest(GympassId, "gym-desconhecido"), default));
    }

    [Fact]
    public async Task SimulateBookingRequested_returns_ok_with_the_booking_service_result()
    {
        var point = new CheckinPoint { TenantId = TenantId, App = IntegrationApp.Wellhub, ExternalId = GymExternalId, Name = "Sandbox" };
        _points.Points.Add(point);
        var wellhubClass = new WellhubClass { TenantId = TenantId, CheckinPointId = point.Id, Name = "Aula", ExternalId = "class-1" };
        _classes.Classes.Add(wellhubClass);
        _slots.Slots.Add(new ClassSlot { TenantId = TenantId, ClassId = wellhubClass.Id, ExternalId = "slot-1", Capacity = 5, BookedCount = 0 });

        var controller = BuildController();
        var result = await controller.SimulateBookingRequested(
            new SimulationController.SimulateBookingRequestedRequest("slot-1", GympassId, "booking-1"), default);

        var dto = Assert.IsType<BookingDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(BookingStatus.Confirmed, dto.Status);
    }

    [Fact]
    public async Task SimulateBookingCanceled_returns_404_for_a_booking_never_requested()
    {
        var point = new CheckinPoint { TenantId = TenantId, App = IntegrationApp.Wellhub, ExternalId = GymExternalId, Name = "Sandbox" };
        _points.Points.Add(point);
        var wellhubClass = new WellhubClass { TenantId = TenantId, CheckinPointId = point.Id, Name = "Aula" };
        _classes.Classes.Add(wellhubClass);
        _slots.Slots.Add(new ClassSlot { TenantId = TenantId, ClassId = wellhubClass.Id, ExternalId = "slot-2", Capacity = 5 });

        var controller = BuildController();
        var result = await controller.SimulateBookingCanceled(
            new SimulationController.SimulateBookingCanceledRequest("slot-2", "booking-nunca-visto", false), default);

        Assert.IsType<NotFoundObjectResult>(result);
    }
}
