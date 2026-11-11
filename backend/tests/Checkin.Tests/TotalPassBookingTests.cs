using System.Net;
using System.Text;
using Checkin.Application.DTOs.Bookings;
using Checkin.Application.Exceptions;
using Checkin.Application.Ports.Integrations;
using Checkin.Application.UseCases.Bookings;
using Checkin.Application.UseCases.CheckinPoints;
using Checkin.Application.UseCases.Students;
using Checkin.Domain.Entities;
using Checkin.Domain.Enums;
using Checkin.Infrastructure.TotalPass;
using Checkin.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Checkin.Tests;

/// <summary>
/// Agenda e reservas do TotalPass: contrato HTTP da API "Booking - New" (adapter) e as regras do
/// BookingService — aula publicada nos dois apps com vagas separadas, ocorrência avulsa por horário
/// em horário de São Paulo, e o registro das reservas que chegam pelo webhook.
/// </summary>
public class TotalPassBookingTests
{
    private const string TenantId = "tenant-1";
    private const string PlaceKey = "place-key";
    private const string WebhookKey = "chave-url";

    // ---- Adapter -------------------------------------------------------------------------

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Url, string? Body, string? Auth)> Calls { get; } = new();
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Calls.Add((request.Method, request.RequestUri!.ToString(), body, request.Headers.Authorization?.ToString()));
            return Respond(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static TotalPassBookingGatewayAdapter Adapter(ScriptedHandler handler) =>
        new(new HttpClient(handler),
            Options.Create(new TotalPassOptions { PartnerApiKey = "partner", WebhookBaseUrl = "https://omniclub.run" }),
            new TotalPassTokenCache(), NullLogger<TotalPassBookingGatewayAdapter>.Instance);

    private static HttpResponseMessage AuthOr(HttpRequestMessage r, Func<HttpResponseMessage> other) =>
        r.RequestUri!.AbsolutePath == "/partner/auth"
            ? Json(HttpStatusCode.Created, """{"partner":{"name":"OmniClub"},"place":{"name":"DVP-HML-266"},"token":"jwt-booking"}""")
            : other();

    [Fact]
    public async Task CreateOccurrence_sends_documented_local_date_and_12h_time()
    {
        var handler = new ScriptedHandler
        {
            Respond = r => AuthOr(r, () => Json(HttpStatusCode.Created, """{"eventId":555,"eventOccurrenceUuid":"occ-uuid-1","slots":8}""")),
        };

        var result = await Adapter(handler).CreateOccurrenceAsync(PlaceKey, new TotalPassOccurrenceRequest(
            "Tênis Iniciante", "Prof. Ana", "Fundamentos", 60, 8, 78422, new DateTime(2026, 11, 16, 18, 0, 0), "slot-abc"));

        Assert.Equal(new TotalPassOccurrence("555", "occ-uuid-1"), result);
        var call = handler.Calls.Last();
        Assert.Equal("https://booking-api.totalpass.com/partner/event-occurrence", call.Url);
        Assert.Equal("Bearer jwt-booking", call.Auth);
        Assert.Contains("\"eventDate\":\"2026-11-16\"", call.Body);
        Assert.Contains("\"startTime\":\"06:00 PM\"", call.Body);
        Assert.Contains("\"timezone\":\"pt-BR\"", call.Body);
        Assert.Contains("\"planId\":78422", call.Body);
        Assert.Contains("\"slots\":8", call.Body);
        Assert.Contains("\"duration\":60", call.Body);
        Assert.Contains("\"responsible\":\"Prof. Ana\"", call.Body);
        Assert.Contains("\"externalReference\":\"slot-abc\"", call.Body);
    }

    [Fact]
    public async Task CreateOccurrence_refused_returns_null_instead_of_throwing()
    {
        var handler = new ScriptedHandler
        {
            Respond = r => AuthOr(r, () => Json(HttpStatusCode.BadRequest, """{"message":["startDate must be a valid ISO 8601 date string"]}""")),
        };

        Assert.Null(await Adapter(handler).CreateOccurrenceAsync(PlaceKey,
            new TotalPassOccurrenceRequest("x", "y", null, 60, 1, 1, DateTime.Now, "s")));
    }

    [Fact]
    public async Task Expired_token_is_renewed_once_and_the_call_retried()
    {
        var calls = 0;
        var handler = new ScriptedHandler
        {
            Respond = r => AuthOr(r, () => ++calls == 1
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : Json(HttpStatusCode.OK, """{"name":"DVP-HML-266","Plans":[{"id":78422,"name":"Plano Portal Developers","code":"R5FK8NDY","deletedAt":null}]}""")),
        };

        var plans = await Adapter(handler).ListPlansAsync(PlaceKey);

        Assert.Equal(new TotalPassPlan(78422, "Plano Portal Developers", "R5FK8NDY"), Assert.Single(plans));
        Assert.Equal(2, handler.Calls.Count(c => c.Url.EndsWith("/partner/auth")));
    }

    [Fact]
    public async Task Subscribe_registers_public_booking_url()
    {
        var handler = new ScriptedHandler { Respond = r => AuthOr(r, () => Json(HttpStatusCode.Created, """{"status":"success","code":200}""")) };

        await Adapter(handler).SubscribeBookingWebhookAsync(PlaceKey, "/api/integrations/totalpass/bookings/abc");

        var call = handler.Calls.Last();
        Assert.Equal("https://booking-api.totalpass.com/partner/webhook/subscribe", call.Url);
        Assert.Equal("""{"webhook_url":"https://omniclub.run/api/integrations/totalpass/bookings/abc"}""", call.Body);
    }

    // ---- BookingService --------------------------------------------------------------------

    private readonly FakeCheckinPointRepository _points = new();
    private readonly FakeClassRepository _classes = new();
    private readonly FakeClassSlotRepository _slots = new();
    private readonly FakeBookingRepository _bookings = new();
    private readonly FakeStudentRepository _students = new();
    private readonly Mock<IWellhubBookingGateway> _wellhub = new();
    private readonly Mock<ITotalPassBookingGateway> _totalPass = new();
    private readonly CheckinPoint _point;

    public TotalPassBookingTests()
    {
        _point = new CheckinPoint
        {
            TenantId = TenantId, Name = "Dynamis Vila da Serra", ExternalId = "547050",
            TotalPassPlaceApiKey = PlaceKey, TotalPassWebhookKey = WebhookKey
        };
        _points.Points.Add(_point);
        _wellhub.SetupGet(g => g.IsConfigured).Returns(true);
        _wellhub.Setup(g => g.CreateClassAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<long>(), It.IsAny<CancellationToken>())).ReturnsAsync("wh-class-1");
        _wellhub.Setup(g => g.CreateSlotAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync("wh-slot-1");
        _totalPass.SetupGet(g => g.IsConfigured).Returns(true);
    }

    private BookingService Service() => new(_classes, _slots, _bookings, _students, _points, _wellhub.Object,
        new FakeCurrentTenantContext { TenantId = TenantId }, _totalPass.Object);

    private async Task<ClassDto> CreateBothAppsClass(BookingService service) =>
        await service.CreateClassAsync(new CreateClassRequest(_point.Id, "Tênis Iniciante", "Fundamentos", ProductId: 1217,
            PublishWellhub: true, PublishTotalPass: true, TotalPassPlanId: 78422, Instructor: "Prof. Ana"));

    [Fact]
    public async Task Class_for_totalpass_requires_a_plan()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Service().CreateClassAsync(
            new CreateClassRequest(_point.Id, "Aula", null, null, PublishWellhub: false, PublishTotalPass: true)));
    }

    [Fact]
    public async Task Slot_is_published_to_both_apps_with_separate_capacities_in_sao_paulo_time()
    {
        TotalPassOccurrenceRequest? sent = null;
        _totalPass.Setup(g => g.CreateOccurrenceAsync(PlaceKey, It.IsAny<TotalPassOccurrenceRequest>(), It.IsAny<CancellationToken>()))
            .Callback<string, TotalPassOccurrenceRequest, CancellationToken>((_, r, _) => sent = r)
            .ReturnsAsync(new TotalPassOccurrence("555", "occ-1"));
        var service = Service();
        var c = await CreateBothAppsClass(service);

        // 21:00 UTC = 18:00 em São Paulo
        var start = new DateTime(2026, 11, 16, 21, 0, 0, DateTimeKind.Utc);
        var slot = await service.CreateSlotAsync(new CreateSlotRequest(c.Id, start, start.AddHours(1), Capacity: 8, TotalPassCapacity: 4));

        Assert.Equal("wh-slot-1", slot.ExternalId);
        Assert.Equal(8, slot.Capacity);
        Assert.Equal("occ-1", slot.TotalPassOccurrenceUuid);
        Assert.Equal(4, slot.TotalPassCapacity);
        Assert.Equal(new DateTime(2026, 11, 16, 18, 0, 0), sent!.StartsAtLocal);
        Assert.Equal(4, sent.Slots);
        Assert.Equal(60, sent.DurationMinutes);
        Assert.Equal("Prof. Ana", sent.Responsible);
        Assert.Equal(slot.Id, sent.ExternalReference);
        Assert.Equal("555", _slots.Slots.Single().TotalPassEventId);
    }

    [Fact]
    public async Task Totalpass_only_class_does_not_touch_wellhub()
    {
        _totalPass.Setup(g => g.CreateOccurrenceAsync(It.IsAny<string>(), It.IsAny<TotalPassOccurrenceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TotalPassOccurrence("556", "occ-2"));
        var service = Service();
        var c = await service.CreateClassAsync(new CreateClassRequest(_point.Id, "Beach", null, null, PublishWellhub: false, PublishTotalPass: true, TotalPassPlanId: 78422));
        var start = DateTime.UtcNow.AddDays(2);

        var slot = await service.CreateSlotAsync(new CreateSlotRequest(c.Id, start, start.AddHours(1), Capacity: 0, TotalPassCapacity: 6));

        Assert.Equal(0, slot.Capacity);
        Assert.Equal("occ-2", slot.TotalPassOccurrenceUuid);
        _wellhub.Verify(g => g.CreateClassAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
        _wellhub.Verify(g => g.CreateSlotAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Sync_publishes_pending_future_slots_to_totalpass()
    {
        _totalPass.SetupSequence(g => g.CreateOccurrenceAsync(It.IsAny<string>(), It.IsAny<TotalPassOccurrenceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TotalPassOccurrence?)null)              // falhou na criação do horário
            .ReturnsAsync(new TotalPassOccurrence("557", "occ-3")); // sobe no Sincronizar
        var service = Service();
        var c = await CreateBothAppsClass(service);
        var start = DateTime.UtcNow.AddDays(3);
        var slot = await service.CreateSlotAsync(new CreateSlotRequest(c.Id, start, start.AddHours(1), 8, 4));
        Assert.Null(slot.TotalPassOccurrenceUuid);

        await service.SyncClassAsync(c.Id);

        Assert.Equal("occ-3", _slots.Slots.Single().TotalPassOccurrenceUuid);
    }

    // ---- Reservas que chegam pelo webhook --------------------------------------------------

    private ClassSlot SeedPublishedSlot()
    {
        var slot = new ClassSlot { TenantId = TenantId, ClassId = "c1", StartsAt = DateTime.UtcNow.AddDays(1), TotalPassCapacity = 4, TotalPassEventId = "555" };
        _slots.Slots.Add(slot);
        return slot;
    }

    private static TotalPassBookingData Booking(string status = "active") =>
        new("555", "slot-tp-1", status, new TotalPassUserInfo("EQ2B3FBK", "Cleveland Wolf", "c@mail.com", "6399907-8947", "66844563680", "cpf"));

    [Fact]
    public async Task New_totalpass_booking_is_recorded_as_confirmed_and_counts_a_spot()
    {
        var slot = SeedPublishedSlot();

        var dto = await Service().HandleTotalPassBookingAsync(WebhookKey, Booking(), "{}");

        Assert.Equal(BookingStatus.Confirmed, dto!.Status);
        Assert.Equal(1, slot.TotalPassBookedCount);
        var booking = Assert.Single(_bookings.Bookings);
        Assert.Equal(IntegrationApp.TotalPass, booking.App);
        Assert.Equal("Cleveland Wolf", Assert.Single(_students.Students).Name);
    }

    [Fact]
    public async Task Repeated_booking_event_is_idempotent_and_cancel_frees_the_spot()
    {
        var slot = SeedPublishedSlot();
        var service = Service();

        await service.HandleTotalPassBookingAsync(WebhookKey, Booking(), "{}");
        await service.HandleTotalPassBookingAsync(WebhookKey, Booking(), "{}");
        Assert.Single(_bookings.Bookings);
        Assert.Equal(1, slot.TotalPassBookedCount);

        await service.HandleTotalPassBookingAsync(WebhookKey, Booking("canceled"), "{}");
        Assert.Equal(BookingStatus.Canceled, _bookings.Bookings.Single().Status);
        Assert.Equal(0, slot.TotalPassBookedCount);
    }

    [Fact]
    public async Task Booking_for_event_not_created_by_omniclub_is_ignored()
    {
        Assert.Null(await Service().HandleTotalPassBookingAsync(WebhookKey, Booking() with { EventId = "999" }, "{}"));
        Assert.Empty(_bookings.Bookings);
    }

    // ---- Conectar também assina o webhook de reservas ---------------------------------------

    [Fact]
    public async Task Connect_subscribes_booking_webhook_and_reports_partial_failure()
    {
        var checkin = new Mock<ITotalPassGateway>();
        checkin.SetupGet(g => g.IsConfigured).Returns(true);
        checkin.Setup(g => g.ConnectPlaceAsync(PlaceKey, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TotalPassConnection("DVP-HML-266", "68362", "https://omniclub.run/x"));
        _totalPass.Setup(g => g.SubscribeBookingWebhookAsync(PlaceKey, CheckinPointService.TotalPassBookingWebhookPathPrefix + WebhookKey, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var service = new CheckinPointService(_points, Mock.Of<IWellhubBookingGateway>(), new FakeCurrentTenantContext { TenantId = TenantId }, checkin.Object, _totalPass.Object);

        var dto = await service.ConnectTotalPassAsync(_point.Id);
        Assert.NotNull(dto.TotalPass!.BookingsConnectedAt);

        _totalPass.Setup(g => g.SubscribeBookingWebhookAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TotalPassException("O TotalPass recusou o cadastro do webhook de reservas (HTTP 500)."));
        _point.TotalPassBookingsConnectedAt = null;

        var ex = await Assert.ThrowsAsync<ConflictException>(() => service.ConnectTotalPassAsync(_point.Id));
        Assert.Contains("Check-ins conectados", ex.Message);
        Assert.NotNull(_point.TotalPassConnectedAt); // check-in ficou conectado mesmo assim
    }
}
