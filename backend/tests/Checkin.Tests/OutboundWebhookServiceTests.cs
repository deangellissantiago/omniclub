using System.Text.Json;
using Checkin.Application.Ports.Integrations;
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
/// Webhook de saída (OutboundWebhookService): contrato do payload/assinatura que o sistema do
/// cliente valida, enfileiramento só de check-in aprovado e política de reenvio.
/// </summary>
public class OutboundWebhookServiceTests
{
    private const string TenantId = "tenant-1";
    private const string Secret = "segredo-de-teste-123";
    private const string Url = "https://clube.example.com/api/omniclub/webhook";

    private readonly FakeTenantRepository _tenants = new();
    private readonly FakeOutboundWebhookDeliveryRepository _deliveries = new();
    private readonly Mock<IOutboundWebhookSender> _sender = new();

    public OutboundWebhookServiceTests()
    {
        _tenants.Tenants.Add(new Tenant { Id = TenantId, Name = "Clube", OutboundWebhookUrl = Url, OutboundWebhookSecret = Secret });
    }

    private OutboundWebhookService BuildService() => new(
        _deliveries, _tenants, _sender.Object, new FakeCurrentTenantContext { TenantId = TenantId },
        NullLogger<OutboundWebhookService>.Instance);

    private static (CheckinRecord, Student, CheckinPoint) ApprovedCheckin()
    {
        var point = new CheckinPoint { TenantId = TenantId, App = IntegrationApp.Wellhub, ExternalId = "547050", Name = "Dynamis Vila da Serra", PricePerCheckinCents = 2500 };
        var student = new Student { TenantId = TenantId, Name = "Ana Souza", Email = "ana@mail.com", Phone = "+5531988887777", WellhubMemberId = "1000000000001" };
        var record = new CheckinRecord
        {
            TenantId = TenantId, CheckinPointId = point.Id, StudentId = student.Id, App = IntegrationApp.Wellhub,
            ExternalMemberRef = "1000000000001", OccurredAt = new DateTime(2026, 10, 16, 21, 0, 0, DateTimeKind.Utc),
            Status = CheckinStatus.Approved
        };
        return (record, student, point);
    }

    [Fact]
    public void Signature_is_hmac_sha256_lowercase_hex_with_prefix()
    {
        // Vetor calculado fora do código: printf '%s' '{"id":"abc"}' | openssl dgst -sha256 -hmac segredo-de-teste-123
        Assert.Equal("sha256=de119344101a4b854319c41541b25319625adc9316422a40e059820fc3b98ed7",
            OutboundWebhookService.ComputeSignature("{\"id\":\"abc\"}", Secret));
    }

    [Fact]
    public async Task Approved_checkin_enqueues_payload_in_the_agreed_format()
    {
        var (record, student, point) = ApprovedCheckin();

        await BuildService().EnqueueCheckinApprovedAsync(record, student, point);

        var delivery = Assert.Single(_deliveries.Deliveries);
        Assert.Equal(record.Id, delivery.EventId);
        Assert.Contains("\"valorRepasse\":25.00", delivery.Payload);
        Assert.Contains("\"ocorridoEm\":\"2026-10-16T18:00:00-03:00\"", delivery.Payload);
        Assert.Contains("Dynamis Vila da Serra", delivery.Payload); // acentos/texto literal, sem \u escapes

        using var json = JsonDocument.Parse(delivery.Payload);
        var root = json.RootElement;
        Assert.Equal(record.Id, root.GetProperty("id").GetString());
        Assert.Equal("checkin.aprovado", root.GetProperty("tipo").GetString());
        Assert.Equal("ana@mail.com", root.GetProperty("atletaEmail").GetString());
        Assert.Equal("Wellhub", root.GetProperty("origem").GetString());
        Assert.Equal("1000000000001", root.GetProperty("wellhubId").GetString());
        Assert.Equal("Ana Souza", root.GetProperty("atletaNome").GetString());
        Assert.Equal("547050", root.GetProperty("unidade").GetProperty("gymId").GetString());
    }

    [Fact]
    public async Task Missing_price_sends_null_valor_repasse()
    {
        var (record, student, point) = ApprovedCheckin();
        point.PricePerCheckinCents = null;

        await BuildService().EnqueueCheckinApprovedAsync(record, student, point);

        Assert.Contains("\"valorRepasse\":null", Assert.Single(_deliveries.Deliveries).Payload);
    }

    [Fact]
    public async Task Does_not_enqueue_rejected_checkin_unconfigured_tenant_or_same_event_twice()
    {
        var service = BuildService();
        var (record, student, point) = ApprovedCheckin();

        record.Status = CheckinStatus.Rejected;
        await service.EnqueueCheckinApprovedAsync(record, student, point);
        Assert.Empty(_deliveries.Deliveries);

        record.Status = CheckinStatus.Approved;
        await service.EnqueueCheckinApprovedAsync(record, student, point);
        await service.EnqueueCheckinApprovedAsync(record, student, point); // reprocessamento
        Assert.Single(_deliveries.Deliveries);

        _tenants.Tenants[0].OutboundWebhookUrl = null;
        var (other, _, _) = ApprovedCheckin();
        await service.EnqueueCheckinApprovedAsync(other, student, point);
        Assert.Single(_deliveries.Deliveries);
    }

    [Fact]
    public async Task Successful_send_is_signed_and_marked_delivered()
    {
        var (record, student, point) = ApprovedCheckin();
        var service = BuildService();
        await service.EnqueueCheckinApprovedAsync(record, student, point);
        OutboundWebhookRequest? sent = null;
        _sender.Setup(s => s.SendAsync(It.IsAny<OutboundWebhookRequest>(), It.IsAny<CancellationToken>()))
            .Callback<OutboundWebhookRequest, CancellationToken>((r, _) => sent = r)
            .ReturnsAsync(new OutboundWebhookSendResult(true, 200, null));

        await service.ProcessDueBatchAsync();

        var delivery = Assert.Single(_deliveries.Deliveries);
        Assert.Equal(OutboundWebhookStatus.Delivered, delivery.Status);
        Assert.Equal(Url, sent!.Url);
        Assert.Equal(OutboundWebhookService.ComputeSignature(delivery.Payload, Secret), sent.Signature);
    }

    [Fact]
    public async Task Failed_send_is_rescheduled_then_gives_up_after_max_attempts()
    {
        var (record, student, point) = ApprovedCheckin();
        var service = BuildService();
        await service.EnqueueCheckinApprovedAsync(record, student, point);
        _sender.Setup(s => s.SendAsync(It.IsAny<OutboundWebhookRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OutboundWebhookSendResult(false, 500, "HTTP 500"));
        var delivery = Assert.Single(_deliveries.Deliveries);

        await service.ProcessDueBatchAsync();
        Assert.Equal(OutboundWebhookStatus.Pending, delivery.Status);
        Assert.True(delivery.NextAttemptAt > DateTime.UtcNow.AddSeconds(50));

        for (var i = 1; i < OutboundWebhookService.MaxAttempts; i++)
        {
            delivery.NextAttemptAt = null; // simula o tempo passando
            await service.ProcessDueBatchAsync();
        }

        Assert.Equal(OutboundWebhookStatus.Failed, delivery.Status);
        Assert.Equal(OutboundWebhookService.MaxAttempts, delivery.Attempts);
        Assert.Equal("HTTP 500", delivery.LastError);
    }

    [Fact]
    public async Task Sender_exception_counts_as_failed_attempt_instead_of_breaking_the_batch()
    {
        var (record, student, point) = ApprovedCheckin();
        var service = BuildService();
        await service.EnqueueCheckinApprovedAsync(record, student, point);
        _sender.Setup(s => s.SendAsync(It.IsAny<OutboundWebhookRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await service.ProcessDueBatchAsync();

        var delivery = Assert.Single(_deliveries.Deliveries);
        Assert.Equal(OutboundWebhookStatus.Pending, delivery.Status);
        Assert.Equal("boom", delivery.LastError);
    }

    [Fact]
    public async Task Enabling_webhook_without_secret_generates_one()
    {
        _tenants.Tenants[0].OutboundWebhookSecret = null;

        var dto = await BuildService().UpdateSettingsAsync(new(Url, null));

        Assert.True(dto.Enabled);
        Assert.Equal(64, dto.Secret!.Length);
    }

    [Fact]
    public async Task Rejects_url_that_is_not_http()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => BuildService().UpdateSettingsAsync(new("ftp://x", null)));
    }

    [Fact]
    public async Task CheckinService_enqueues_webhook_when_wellhub_approves()
    {
        var points = new FakeCheckinPointRepository();
        var (_, _, point) = ApprovedCheckin();
        points.Points.Add(point);
        var gateway = new Mock<IWellhubGateway>();
        gateway.SetupGet(g => g.IsConfigured).Returns(false); // sem credenciais = aprova direto

        var checkinService = new CheckinService(new FakeCheckinRecordRepository(), points, new FakeStudentRepository(),
            gateway.Object, new FakeCurrentTenantContext { TenantId = TenantId }, BuildService());
        await checkinService.RegisterWellhubCheckinAsync("547050", "1000000000009", "ext-1", DateTime.UtcNow, "{}");

        Assert.Single(_deliveries.Deliveries);
    }

    [Fact]
    public async Task RetryMany_requeues_selected_deliveries_of_the_tenant_only()
    {
        OutboundWebhookDelivery Failed(string tenant) => new()
        {
            TenantId = tenant, EventId = Guid.NewGuid().ToString(), EventType = "checkin.aprovado", Payload = "{}",
            Status = OutboundWebhookStatus.Failed, Attempts = OutboundWebhookService.MaxAttempts, LastError = "HTTP 500"
        };
        var mine1 = Failed(TenantId);
        var mine2 = Failed(TenantId);
        var notSelected = Failed(TenantId);
        var otherTenant = Failed("outro-tenant");
        _deliveries.Deliveries.AddRange(new[] { mine1, mine2, notSelected, otherTenant });

        var requeued = await BuildService().RetryManyAsync(new[] { mine1.Id, mine2.Id, mine2.Id, otherTenant.Id, "nao-existe" });

        Assert.Equal(2, requeued);
        foreach (var d in new[] { mine1, mine2 })
        {
            Assert.Equal(OutboundWebhookStatus.Pending, d.Status);
            Assert.Equal(0, d.Attempts);
            Assert.Null(d.NextAttemptAt);
        }
        Assert.Equal(OutboundWebhookStatus.Failed, notSelected.Status);
        Assert.Equal(OutboundWebhookStatus.Failed, otherTenant.Status);
    }

    [Fact]
    public async Task RetryMany_requires_at_least_one_id()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => BuildService().RetryManyAsync(Array.Empty<string>()));
    }

    [Fact]
    public async Task ListDeliveries_filters_by_status()
    {
        _deliveries.Deliveries.Add(new OutboundWebhookDelivery { TenantId = TenantId, Status = OutboundWebhookStatus.Failed });
        _deliveries.Deliveries.Add(new OutboundWebhookDelivery { TenantId = TenantId, Status = OutboundWebhookStatus.Delivered });

        var failed = await BuildService().ListDeliveriesAsync(OutboundWebhookStatus.Failed);

        Assert.Equal(OutboundWebhookStatus.Failed, Assert.Single(failed).Status);
    }
}
