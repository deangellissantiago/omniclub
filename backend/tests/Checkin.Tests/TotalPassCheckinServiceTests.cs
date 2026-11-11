using Checkin.Application.DTOs.Checkins;
using Checkin.Application.Exceptions;
using Checkin.Application.Ports.Integrations;
using Checkin.Application.UseCases.CheckinPoints;
using Checkin.Application.UseCases.Checkins;
using Checkin.Domain.Entities;
using Checkin.Domain.Enums;
using Checkin.Tests.Fakes;
using Moq;
using Xunit;

namespace Checkin.Tests;

/// <summary>Check-in TotalPass (CheckinService.RegisterTotalPassCheckinAsync) e a conexão da
/// unidade (CheckinPointService.ConnectTotalPassAsync).</summary>
public class TotalPassCheckinServiceTests
{
    private const string TenantId = "tenant-1";
    private const string WebhookKey = "chave-da-url";
    private const string ConfirmationUrl = "https://admin.totalpass.com/api/v1/webhook_confirmations/TOK123==";

    private readonly FakeCheckinPointRepository _points = new();
    private readonly FakeStudentRepository _students = new();
    private readonly FakeCheckinRecordRepository _records = new();
    private readonly Mock<ITotalPassGateway> _gateway = new();
    private readonly CheckinPoint _point;

    public TotalPassCheckinServiceTests()
    {
        _point = new CheckinPoint
        {
            TenantId = TenantId, TotalPassPlaceId = "68362", Name = "Dynamis Vila da Serra",
            TotalPassPlaceApiKey = "place-key", TotalPassWebhookKey = WebhookKey
        };
        _points.Points.Add(_point);
        _gateway.SetupGet(g => g.IsConfigured).Returns(true);
    }

    private CheckinService BuildService() => new(_records, _points, _students, Mock.Of<IWellhubGateway>(),
        new FakeCurrentTenantContext { TenantId = TenantId }, outboundWebhooks: null, totalPassGateway: _gateway.Object);

    private static TotalPassCheckinData Data(string code = "EQ2B3FBK", string cpf = "668.445.636-80") => new(
        ConfirmationUrl, new DateTime(2026, 11, 11, 20, 54, 16, DateTimeKind.Utc), code, "Cleveland Wolf", "cleveland@mail.com",
        "6399907-8947", cpf, "cpf");

    [Fact]
    public async Task Confirmed_checkin_is_approved_and_pre_registers_the_student()
    {
        _gateway.Setup(g => g.ConfirmCheckinAsync("place-key", ConfirmationUrl, It.IsAny<CancellationToken>())).ReturnsAsync(TotalPassConfirmation.Ok);

        var dto = await BuildService().RegisterTotalPassCheckinAsync(WebhookKey, Data(), "{}");

        Assert.Equal(CheckinStatus.Approved, dto.Status);
        var student = Assert.Single(_students.Students);
        Assert.Equal("Cleveland Wolf", student.Name);
        Assert.Equal("EQ2B3FBK", student.TotalPassMemberId);
        Assert.Equal("66844563680", student.Document);
        var record = Assert.Single(_records.Records);
        Assert.Equal(IntegrationApp.TotalPass, record.App);
        Assert.Equal("totalpass:TOK123==", record.ExternalCheckinId);
    }

    [Fact]
    public async Task Expired_checkin_is_rejected_with_reason()
    {
        _gateway.Setup(g => g.ConfirmCheckinAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TotalPassConfirmation.Rejected("Tempo de check-in expirado!"));

        var dto = await BuildService().RegisterTotalPassCheckinAsync(WebhookKey, Data(), "{}");

        Assert.Equal(CheckinStatus.Rejected, dto.Status);
        Assert.Equal("Tempo de check-in expirado!", dto.RejectionReason);
    }

    [Fact]
    public async Task Existing_student_with_same_cpf_is_linked_instead_of_duplicated()
    {
        var existing = new Student { TenantId = TenantId, Name = "Cleveland W.", Document = "66844563680", WellhubMemberId = "100" };
        _students.Students.Add(existing);
        _gateway.Setup(g => g.ConfirmCheckinAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(TotalPassConfirmation.Ok);

        await BuildService().RegisterTotalPassCheckinAsync(WebhookKey, Data(), "{}");

        Assert.Single(_students.Students);
        Assert.Equal("EQ2B3FBK", existing.TotalPassMemberId);
        Assert.Equal(existing.Id, _records.Records.Single().StudentId);
    }

    [Fact]
    public async Task Same_event_twice_confirms_and_records_only_once()
    {
        _gateway.Setup(g => g.ConfirmCheckinAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(TotalPassConfirmation.Ok);
        var service = BuildService();

        await service.RegisterTotalPassCheckinAsync(WebhookKey, Data(), "{}");
        await service.RegisterTotalPassCheckinAsync(WebhookKey, Data(), "{}");

        Assert.Single(_records.Records);
        _gateway.Verify(g => g.ConfirmCheckinAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Without_totalpass_configured_checkin_is_rejected_not_approved()
    {
        _gateway.SetupGet(g => g.IsConfigured).Returns(false);

        var dto = await BuildService().RegisterTotalPassCheckinAsync(WebhookKey, Data(), "{}");

        Assert.Equal(CheckinStatus.Rejected, dto.Status);
        _gateway.Verify(g => g.ConfirmCheckinAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Unknown_webhook_key_is_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => BuildService().RegisterTotalPassCheckinAsync("outra-chave", Data(), "{}"));
    }

    // ---- Conexão da unidade --------------------------------------------------------------

    private CheckinPointService BuildPointService() =>
        new(_points, Mock.Of<IWellhubBookingGateway>(), new FakeCurrentTenantContext { TenantId = TenantId }, _gateway.Object);

    [Fact]
    public async Task Connect_registers_webhook_path_with_secret_key_and_fills_place_id()
    {
        _point.ExternalId = "";
        _gateway.Setup(g => g.ConnectPlaceAsync("place-key", CheckinPointService.TotalPassWebhookPathPrefix + WebhookKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TotalPassConnection("DVP-HML-266", "68362", "https://omniclub.run/x"));

        var dto = await BuildPointService().ConnectTotalPassAsync(_point.Id);

        Assert.Equal("68362", dto.TotalPass!.PlaceId);
        Assert.NotNull(dto.TotalPass!.ConnectedAt);
        Assert.Equal("DVP-HML-266", dto.TotalPass.PlaceName);
        Assert.Equal("…-key", dto.TotalPass.PlaceApiKeyHint);
    }

    [Fact]
    public async Task Connect_surfaces_totalpass_errors_as_conflict()
    {
        _gateway.Setup(g => g.ConnectPlaceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TotalPassException("O TotalPass recusou as chaves."));

        var ex = await Assert.ThrowsAsync<ConflictException>(() => BuildPointService().ConnectTotalPassAsync(_point.Id));
        Assert.Equal("O TotalPass recusou as chaves.", ex.Message);
    }

    [Fact]
    public async Task Changing_place_key_requires_reconnecting()
    {
        _point.TotalPassConnectedAt = DateTime.UtcNow;

        await BuildPointService().UpdateAsync(_point.Id, new(_point.Name, true, TotalPassEnabled: true, TotalPassPlaceApiKey: "nova-chave"));

        Assert.Equal("nova-chave", _point.TotalPassPlaceApiKey);
        Assert.Null(_point.TotalPassConnectedAt);
    }

    // ---- Unidade com os dois apps --------------------------------------------------------

    [Fact]
    public async Task One_unit_can_accept_wellhub_and_totalpass_with_separate_prices()
    {
        var dto = await BuildPointService().CreateAsync(new("Dynamis Pilar", true,
            ExternalId: "804587", PricePerCheckinCents: 1500,
            TotalPassEnabled: true, TotalPassPlaceApiKey: "chave-pilar", TotalPassPricePerCheckinCents: 2000));

        Assert.Equal(new[] { IntegrationApp.Wellhub, IntegrationApp.TotalPass }, dto.Apps);
        Assert.Equal("804587", dto.ExternalId);
        Assert.Equal(1500, dto.PricePerCheckinCents);
        Assert.Equal(2000, dto.TotalPass!.PricePerCheckinCents);

        var point = _points.Points.Single(p => p.Id == dto.Id);
        Assert.Equal(1500, point.PriceFor(IntegrationApp.Wellhub));
        Assert.Equal(2000, point.PriceFor(IntegrationApp.TotalPass));
        Assert.NotNull(point.TotalPassWebhookKey);
    }

    [Fact]
    public async Task Unit_needs_at_least_one_app()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => BuildPointService().CreateAsync(new("Sem app", true)));
    }

    [Fact]
    public async Task Turning_totalpass_off_clears_its_keys_but_keeps_wellhub()
    {
        _point.ExternalId = "547050";
        _point.TotalPassConnectedAt = DateTime.UtcNow;

        var dto = await BuildPointService().UpdateAsync(_point.Id, new(_point.Name, true, ExternalId: "547050", TotalPassEnabled: false));

        Assert.Equal(new[] { IntegrationApp.Wellhub }, dto.Apps);
        Assert.Null(dto.TotalPass);
        Assert.Null(_point.TotalPassPlaceApiKey);
        Assert.Null(_point.TotalPassWebhookKey); // webhook antigo no TotalPass passa a dar 404 aqui
    }

    [Fact]
    public async Task Wellhub_gym_id_must_be_unique_across_units()
    {
        _points.Points.Add(new CheckinPoint { TenantId = TenantId, ExternalId = "500977", Name = "Santa Lúcia" });

        await Assert.ThrowsAsync<ConflictException>(() => BuildPointService().CreateAsync(new("Outra", true, ExternalId: "500977")));
    }
}
