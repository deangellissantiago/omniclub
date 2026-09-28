using Checkin.Application.Ports.Integrations;
using Checkin.Application.UseCases.Bookings;
using Checkin.Domain.Entities;
using Checkin.Domain.Enums;
using Checkin.Tests.Fakes;
using Moq;
using Xunit;

namespace Checkin.Tests;

/// <summary>
/// Cobre "Gestão de Reservas" (BookingService): confirma reserva só se há vaga, rejeita se
/// lotado — mesma filosofia de aprovação automática do check-in, sem fila de espera.
/// </summary>
public class BookingServiceTests
{
    private const string TenantId = "tenant-1";
    private const string GymExternalId = "609";
    private const string ClassExternalId = "wellhub-class-1";
    private const string SlotExternalId = "wellhub-slot-1";
    private const string GympassId = "member-abc-123";

    private readonly FakeCheckinPointRepository _points = new();
    private readonly FakeClassRepository _classes = new();
    private readonly FakeClassSlotRepository _slots = new();
    private readonly FakeBookingRepository _bookings = new();
    private readonly FakeStudentRepository _students = new();
    private readonly Mock<IWellhubBookingGateway> _gateway = new();

    private BookingService BuildService() => new(
        _classes, _slots, _bookings, _students, _points, _gateway.Object, new FakeCurrentTenantContext { TenantId = TenantId });

    private ClassSlot SeedSlot(int capacity, int bookedCount = 0)
    {
        var point = new CheckinPoint { TenantId = TenantId, App = IntegrationApp.Wellhub, ExternalId = GymExternalId, Name = "Sandbox" };
        _points.Points.Add(point);

        var wellhubClass = new WellhubClass { TenantId = TenantId, CheckinPointId = point.Id, Name = "Tênis Iniciante", ExternalId = ClassExternalId };
        _classes.Classes.Add(wellhubClass);

        var slot = new ClassSlot
        {
            TenantId = TenantId, ClassId = wellhubClass.Id, ExternalId = SlotExternalId,
            StartsAt = DateTime.UtcNow.AddDays(1), EndsAt = DateTime.UtcNow.AddDays(1).AddHours(1),
            Capacity = capacity, BookedCount = bookedCount
        };
        _slots.Slots.Add(slot);
        return slot;
    }

    [Fact]
    public async Task Confirms_booking_when_slot_has_available_capacity()
    {
        var slot = SeedSlot(capacity: 10, bookedCount: 3);
        _gateway.SetupGet(g => g.IsConfigured).Returns(true);

        var service = BuildService();
        var dto = await service.HandleBookingRequestedAsync(SlotExternalId, GympassId, "booking-1", DateTime.UtcNow, "{}");

        Assert.Equal(BookingStatus.Confirmed, dto.Status);
        Assert.Equal(4, slot.BookedCount);
        _gateway.Verify(g => g.ConfirmBookingAsync(GymExternalId, ClassExternalId, "booking-1", It.IsAny<CancellationToken>()), Times.Once);
        _gateway.Verify(g => g.UpdateSlotVacancyAsync(GymExternalId, ClassExternalId, SlotExternalId, 10, 4, It.IsAny<CancellationToken>()), Times.Once);
        _gateway.Verify(g => g.RejectBookingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Rejects_booking_when_slot_is_full()
    {
        SeedSlot(capacity: 5, bookedCount: 5);
        _gateway.SetupGet(g => g.IsConfigured).Returns(true);

        var service = BuildService();
        var dto = await service.HandleBookingRequestedAsync(SlotExternalId, GympassId, "booking-2", DateTime.UtcNow, "{}");

        Assert.Equal(BookingStatus.Rejected, dto.Status);
        _gateway.Verify(g => g.RejectBookingAsync(GymExternalId, ClassExternalId, "booking-2", It.IsAny<CancellationToken>()), Times.Once);
        _gateway.Verify(g => g.ConfirmBookingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Works_locally_without_calling_gateway_when_not_configured()
    {
        SeedSlot(capacity: 5, bookedCount: 0);
        _gateway.SetupGet(g => g.IsConfigured).Returns(false);

        var service = BuildService();
        var dto = await service.HandleBookingRequestedAsync(SlotExternalId, GympassId, "booking-3", DateTime.UtcNow, "{}");

        Assert.Equal(BookingStatus.Confirmed, dto.Status);
        _gateway.Verify(g => g.ConfirmBookingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Is_idempotent_for_repeated_booking_requested_events()
    {
        var slot = SeedSlot(capacity: 5, bookedCount: 0);
        _gateway.SetupGet(g => g.IsConfigured).Returns(true);

        var service = BuildService();
        var first = await service.HandleBookingRequestedAsync(SlotExternalId, GympassId, "booking-retry", DateTime.UtcNow, "{}");
        var second = await service.HandleBookingRequestedAsync(SlotExternalId, GympassId, "booking-retry", DateTime.UtcNow, "{}");

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, _bookings.CreateCallCount);
        Assert.Equal(1, slot.BookedCount); // não conta a vaga duas vezes
    }

    [Fact]
    public async Task Throws_not_found_for_unknown_slot()
    {
        var service = BuildService();

        await Assert.ThrowsAsync<Checkin.Application.Exceptions.NotFoundException>(() =>
            service.HandleBookingRequestedAsync("slot-desconhecido", GympassId, "booking-4", DateTime.UtcNow, "{}"));
    }

    [Fact]
    public async Task Cancel_releases_the_vacancy_of_a_previously_confirmed_booking()
    {
        var slot = SeedSlot(capacity: 5, bookedCount: 0);
        _gateway.SetupGet(g => g.IsConfigured).Returns(true);
        var service = BuildService();
        await service.HandleBookingRequestedAsync(SlotExternalId, GympassId, "booking-5", DateTime.UtcNow, "{}");
        Assert.Equal(1, slot.BookedCount);

        var dto = await service.HandleBookingCanceledAsync(SlotExternalId, "booking-5", lateCancel: false);

        Assert.NotNull(dto);
        Assert.Equal(BookingStatus.Canceled, dto!.Status);
        Assert.Equal(0, slot.BookedCount);
        _gateway.Verify(g => g.UpdateSlotVacancyAsync(GymExternalId, ClassExternalId, SlotExternalId, 5, 0, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Late_cancel_marks_the_booking_as_late_canceled()
    {
        var slot = SeedSlot(capacity: 5, bookedCount: 0);
        _gateway.SetupGet(g => g.IsConfigured).Returns(true);
        var service = BuildService();
        await service.HandleBookingRequestedAsync(SlotExternalId, GympassId, "booking-6", DateTime.UtcNow, "{}");

        var dto = await service.HandleBookingCanceledAsync(SlotExternalId, "booking-6", lateCancel: true);

        Assert.Equal(BookingStatus.LateCanceled, dto!.Status);
    }

    [Fact]
    public async Task Cancel_returns_null_for_a_booking_we_never_saw_requested()
    {
        SeedSlot(capacity: 5, bookedCount: 0);
        var service = BuildService();

        var dto = await service.HandleBookingCanceledAsync(SlotExternalId, "booking-nunca-visto", lateCancel: false);

        Assert.Null(dto);
    }

    [Fact]
    public async Task ListBookings_resolves_student_class_slot_and_checkin_point()
    {
        var slot = SeedSlot(capacity: 10);
        var student = new Student { TenantId = TenantId, Name = "Ana", WellhubMemberId = GympassId };
        _students.Students.Add(student);
        _bookings.Bookings.Add(new Booking
        {
            TenantId = TenantId, SlotId = slot.Id, StudentId = student.Id, GympassId = GympassId,
            ExternalBookingId = "booking-10", Status = BookingStatus.Confirmed, RequestedAt = DateTime.UtcNow
        });

        var items = await BuildService().ListBookingsAsync(null, null);

        var item = Assert.Single(items);
        Assert.Equal("Ana", item.StudentName);
        Assert.Equal("Tênis Iniciante", item.ClassName);
        Assert.Equal("Sandbox", item.CheckinPointName);
        Assert.Equal(slot.StartsAt, item.SlotStartsAt);
    }

    [Fact]
    public async Task SyncClass_creates_missing_class_and_only_future_unsynced_slots_in_wellhub()
    {
        var point = new CheckinPoint { TenantId = TenantId, App = IntegrationApp.Wellhub, ExternalId = GymExternalId, Name = "Sandbox" };
        _points.Points.Add(point);
        var wellhubClass = new WellhubClass { TenantId = TenantId, CheckinPointId = point.Id, Name = "Tênis", ProductId = 1217 };
        _classes.Classes.Add(wellhubClass);
        var future = new ClassSlot { TenantId = TenantId, ClassId = wellhubClass.Id, StartsAt = DateTime.UtcNow.AddDays(2), EndsAt = DateTime.UtcNow.AddDays(2).AddHours(1), Capacity = 8 };
        var past = new ClassSlot { TenantId = TenantId, ClassId = wellhubClass.Id, StartsAt = DateTime.UtcNow.AddDays(-2), EndsAt = DateTime.UtcNow.AddDays(-2).AddHours(1), Capacity = 8 };
        _slots.Slots.AddRange(new[] { future, past });

        _gateway.SetupGet(g => g.IsConfigured).Returns(true);
        _gateway.Setup(g => g.CreateClassAsync(GymExternalId, "Tênis", null, 1217, It.IsAny<CancellationToken>())).ReturnsAsync("class-99");
        _gateway.Setup(g => g.CreateSlotAsync(GymExternalId, "class-99", 1217, future.StartsAt, future.EndsAt, 8, It.IsAny<CancellationToken>())).ReturnsAsync("slot-99");

        var dto = await BuildService().SyncClassAsync(wellhubClass.Id);

        Assert.Equal("class-99", dto.ExternalId);
        Assert.Equal("slot-99", future.ExternalId);
        Assert.Null(past.ExternalId);
        _gateway.Verify(g => g.CreateSlotAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), past.StartsAt, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncClass_fails_clearly_when_wellhub_is_not_configured()
    {
        var slot = SeedSlot(capacity: 5);
        _gateway.SetupGet(g => g.IsConfigured).Returns(false);

        await Assert.ThrowsAsync<Checkin.Application.Exceptions.ConflictException>(() => BuildService().SyncClassAsync(slot.ClassId));
    }

    [Fact]
    public async Task CreateSlot_rejects_end_before_start()
    {
        var slot = SeedSlot(capacity: 5);
        var start = DateTime.UtcNow.AddDays(1);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            BuildService().CreateSlotAsync(new Checkin.Application.DTOs.Bookings.CreateSlotRequest(slot.ClassId, start, start.AddMinutes(-30), 5)));
    }

    [Fact]
    public async Task Booking_from_unknown_member_pre_registers_student_with_webhook_data()
    {
        SeedSlot(capacity: 5);
        _gateway.SetupGet(g => g.IsConfigured).Returns(false);

        var userInfo = new Checkin.Application.DTOs.Checkins.WellhubUserInfo("Mike Hightower", null, "mike@mail.com", "+15165930060");
        var dto = await BuildService().HandleBookingRequestedAsync(SlotExternalId, GympassId, "booking-11", DateTime.UtcNow, "{}", userInfo);

        var student = Assert.Single(_students.Students);
        Assert.Equal("Mike Hightower", student.Name);
        Assert.Equal(TenantId, student.TenantId);
        Assert.Equal(GympassId, student.WellhubMemberId);
        Assert.Equal(student.Id, dto.StudentId);
        Assert.Equal("Mike Hightower", dto.StudentName);
    }
}
