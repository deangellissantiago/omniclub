using Checkin.Application.Ports.Integrations;
using Checkin.Application.UseCases.Bookings;
using Checkin.Application.UseCases.Checkins;
using Checkin.Application.UseCases.Webhooks;
using Checkin.Domain.Entities;
using Checkin.Domain.Enums;
using Checkin.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Checkin.Tests;

/// <summary>
/// Cobre o processamento em background dos webhooks do Wellhub (ver WebhookEvent, "Por quê" — o
/// controller só grava o evento cru e responde 202; quem interpreta e dispara
/// CheckinService/BookingService de verdade é WebhookProcessingService, aqui testado
/// isoladamente do HTTP).
/// </summary>
public class WebhookProcessingServiceTests
{
    private const string TenantId = "tenant-1";
    private const string GymExternalId = "609";

    private readonly FakeWebhookEventRepository _events = new();
    private readonly FakeCheckinPointRepository _points = new();
    private readonly FakeStudentRepository _students = new();
    private readonly FakeCheckinRecordRepository _checkins = new();
    private readonly FakeClassRepository _classes = new();
    private readonly FakeClassSlotRepository _slots = new();
    private readonly FakeBookingRepository _bookings = new();
    private readonly Mock<IWellhubGateway> _checkinGateway = new();
    private readonly Mock<IWellhubBookingGateway> _bookingGateway = new();
    private readonly FakeCurrentTenantContext _tenantContext = new() { TenantId = TenantId };

    private WebhookProcessingService BuildService()
    {
        var checkinService = new CheckinService(_checkins, _points, _students, _checkinGateway.Object, _tenantContext);
        var bookingService = new BookingService(_classes, _slots, _bookings, _students, _points, _bookingGateway.Object, _tenantContext);
        return new WebhookProcessingService(_events, checkinService, bookingService, NullLogger<WebhookProcessingService>.Instance);
    }

    private CheckinPoint SeedCheckinPoint()
    {
        var point = new CheckinPoint { TenantId = TenantId, App = IntegrationApp.Wellhub, ExternalId = GymExternalId, Name = "Sandbox" };
        _points.Points.Add(point);
        return point;
    }

    private WebhookEvent Enqueue(string eventType, string rawPayload)
    {
        var evt = new WebhookEvent { Provider = "Wellhub", EventType = eventType, RawPayload = rawPayload };
        _events.Events.Add(evt);
        return evt;
    }

    private const string CheckinPayload =
        """{"event_type":"checkin","event_data":{"user":{"unique_token":"member-1","first_name":"Ana","last_name":"Sandbox","email":"ana@example.com","phone_number":"+5531900000000"},"location":{"lat":-19.9,"lon":-43.9},"gym":{"id":609,"title":"Sandbox","product":{"id":1,"description":"Gym"}},"timestamp":1700000000}}""";

    [Fact]
    public async Task Processes_a_checkin_event_and_marks_it_processed()
    {
        SeedCheckinPoint();
        _checkinGateway.SetupGet(g => g.IsConfigured).Returns(false); // fallback local (sem credencial) = aprova direto
        var evt = Enqueue("checkin", CheckinPayload);

        var processedCount = await BuildService().ProcessPendingBatchAsync();

        Assert.Equal(1, processedCount);
        Assert.Equal(WebhookEventStatus.Processed, evt.Status);
        Assert.NotNull(evt.ProcessedAt);
        Assert.Single(_checkins.Records);
        Assert.Equal(CheckinStatus.Approved, _checkins.Records[0].Status);
        Assert.Single(_students.Students); // pré-cadastro automático (tem first_name)
    }

    [Fact]
    public async Task Processes_a_booking_requested_event_and_confirms_when_theres_capacity()
    {
        var point = SeedCheckinPoint();
        var wellhubClass = new WellhubClass { TenantId = TenantId, CheckinPointId = point.Id, Name = "Tênis", ExternalId = "600001" };
        _classes.Classes.Add(wellhubClass);
        var slot = new ClassSlot { TenantId = TenantId, ClassId = wellhubClass.Id, ExternalId = "500001", Capacity = 5, BookedCount = 0 };
        _slots.Slots.Add(slot);
        _bookingGateway.SetupGet(g => g.IsConfigured).Returns(false);

        var payload = """{"event_type":"booking-requested","event_data":{"user":{"unique_token":"member-1","name":"Ana"},"slot":{"id":500001,"gym_id":609,"class_id":600001,"booking_number":"BK-1"},"timestamp":1700000000,"event_id":"evt-1"}}""";
        var evt = Enqueue("booking-requested", payload);

        await BuildService().ProcessPendingBatchAsync();

        Assert.Equal(WebhookEventStatus.Processed, evt.Status);
        Assert.Single(_bookings.Bookings);
        Assert.Equal(BookingStatus.Confirmed, _bookings.Bookings[0].Status);
        Assert.Equal(1, slot.BookedCount);
    }

    [Fact]
    public async Task Processes_a_booking_canceled_event_and_releases_the_vacancy()
    {
        var point = SeedCheckinPoint();
        var wellhubClass = new WellhubClass { TenantId = TenantId, CheckinPointId = point.Id, Name = "Tênis", ExternalId = "600001" };
        _classes.Classes.Add(wellhubClass);
        var slot = new ClassSlot { TenantId = TenantId, ClassId = wellhubClass.Id, ExternalId = "500001", Capacity = 5, BookedCount = 0 };
        _slots.Slots.Add(slot);
        _bookingGateway.SetupGet(g => g.IsConfigured).Returns(false);

        var requestedPayload = """{"event_type":"booking-requested","event_data":{"user":{"unique_token":"member-1","name":"Ana"},"slot":{"id":500001,"gym_id":609,"class_id":600001,"booking_number":"BK-1"},"timestamp":1700000000,"event_id":"evt-1"}}""";
        var canceledPayload = """{"event_type":"booking-canceled","event_data":{"slot":{"id":500001,"gym_id":609,"class_id":600001,"booking_number":"BK-1"},"timestamp":1700000100,"event_id":"evt-2"}}""";
        Enqueue("booking-requested", requestedPayload);
        var cancelEvt = Enqueue("booking-canceled", canceledPayload);

        var service = BuildService();
        await service.ProcessPendingBatchAsync(); // processa os dois na mesma leva (fila ordenada por ReceivedAt)

        Assert.Equal(WebhookEventStatus.Processed, cancelEvt.Status);
        Assert.Equal(BookingStatus.Canceled, _bookings.Bookings[0].Status);
        Assert.Equal(0, slot.BookedCount);
    }

    [Fact]
    public async Task Marks_an_unknown_event_type_as_processed_without_dispatching_anything()
    {
        var evt = Enqueue("some-future-event", """{"event_type":"some-future-event"}""");

        var processedCount = await BuildService().ProcessPendingBatchAsync();

        Assert.Equal(1, processedCount);
        Assert.Equal(WebhookEventStatus.Processed, evt.Status);
    }

    [Fact]
    public async Task Marks_a_malformed_payload_as_processed_instead_of_retrying_forever()
    {
        // "checkin" sem gym/user — formato inesperado, mas não é uma falha transitória (retentar
        // não vai fazer o payload virar válido) então não faz sentido reprocessar.
        var evt = Enqueue("checkin", """{"event_type":"checkin","event_data":{}}""");

        await BuildService().ProcessPendingBatchAsync();

        Assert.Equal(WebhookEventStatus.Processed, evt.Status);
        Assert.Empty(_checkins.Records);
    }

    [Fact]
    public async Task Retries_with_backoff_when_processing_fails_and_gives_up_after_max_attempts()
    {
        // Nenhum CheckinPoint cadastrado para o gym 609 -> RegisterWellhubCheckinAsync lança
        // NotFoundException toda vez — simula uma falha persistente (ex.: Wellhub fora do ar).
        var evt = Enqueue("checkin", CheckinPayload);
        var service = BuildService();

        for (var attempt = 1; attempt <= WebhookProcessingService.MaxAttempts; attempt++)
        {
            await service.ProcessPendingBatchAsync();

            Assert.Equal(attempt, evt.Attempts);
            if (attempt < WebhookProcessingService.MaxAttempts)
            {
                Assert.Equal(WebhookEventStatus.Pending, evt.Status);
                Assert.NotNull(evt.NextAttemptAt); // backoff -- não fica disponível pra já tentar de novo
                Assert.NotNull(evt.LastError);
                evt.NextAttemptAt = null; // simula "o tempo de backoff passou" pra forçar a próxima tentativa no teste
            }
        }

        Assert.Equal(WebhookEventStatus.Failed, evt.Status);
        Assert.Equal(WebhookProcessingService.MaxAttempts, evt.Attempts);
    }

    [Fact]
    public async Task ProcessPendingBatchAsync_returns_zero_when_the_queue_is_empty()
    {
        var processedCount = await BuildService().ProcessPendingBatchAsync();

        Assert.Equal(0, processedCount);
    }
}
