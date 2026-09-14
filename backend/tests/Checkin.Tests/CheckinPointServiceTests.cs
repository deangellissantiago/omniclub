using Checkin.Application.DTOs.Bookings;
using Checkin.Application.Exceptions;
using Checkin.Application.Ports.Integrations;
using Checkin.Application.UseCases.CheckinPoints;
using Checkin.Domain.Entities;
using Checkin.Domain.Enums;
using Checkin.Tests.Fakes;
using Moq;
using Xunit;

namespace Checkin.Tests;

/// <summary>
/// Cobre CheckinPointService.SyncProductsAsync — "buscar os produtos e vincular aos pontos
/// cadastrados pra ficarem visíveis" (GET /setup/v1/gyms/:gym_id/products).
/// </summary>
public class CheckinPointServiceTests
{
    private const string TenantId = "tenant-1";
    private const string GymExternalId = "609";

    private readonly FakeCheckinPointRepository _points = new();
    private readonly Mock<IWellhubBookingGateway> _gateway = new();

    private CheckinPointService BuildService() =>
        new(_points, _gateway.Object, new FakeCurrentTenantContext { TenantId = TenantId });

    private CheckinPoint SeedPoint()
    {
        var point = new CheckinPoint { TenantId = TenantId, App = IntegrationApp.Wellhub, ExternalId = GymExternalId, Name = "Sandbox" };
        _points.Points.Add(point);
        return point;
    }

    [Fact]
    public async Task Syncs_products_from_wellhub_and_links_them_to_the_point()
    {
        var point = SeedPoint();
        _gateway.SetupGet(g => g.IsConfigured).Returns(true);
        _gateway.Setup(g => g.ListProductsAsync(GymExternalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new WellhubProductDto(1217, "Outdoor Training", false),
                new WellhubProductDto(1218, "Virtual Class", true)
            });

        var service = BuildService();
        var dto = await service.SyncProductsAsync(point.Id);

        Assert.Equal(2, dto.Products.Count);
        Assert.Contains(dto.Products, p => p.ProductId == 1217 && p.Name == "Outdoor Training" && !p.Virtual);
        Assert.Contains(dto.Products, p => p.ProductId == 1218 && p.Virtual);
        // e fica vinculado no cadastro (visível numa próxima listagem, sem precisar buscar de novo)
        Assert.Equal(2, point.Products.Count);
    }

    [Fact]
    public async Task Throws_not_found_for_unknown_checkin_point()
    {
        var service = BuildService();

        await Assert.ThrowsAsync<NotFoundException>(() => service.SyncProductsAsync("id-desconhecido"));
    }

    [Fact]
    public async Task Throws_when_wellhub_gateway_is_not_configured()
    {
        var point = SeedPoint();
        _gateway.SetupGet(g => g.IsConfigured).Returns(false);

        var service = BuildService();

        await Assert.ThrowsAsync<ArgumentException>(() => service.SyncProductsAsync(point.Id));
    }

    [Fact]
    public async Task Throws_when_wellhub_call_fails()
    {
        var point = SeedPoint();
        _gateway.SetupGet(g => g.IsConfigured).Returns(true);
        _gateway.Setup(g => g.ListProductsAsync(GymExternalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WellhubProductDto>?)null);

        var service = BuildService();

        await Assert.ThrowsAsync<ArgumentException>(() => service.SyncProductsAsync(point.Id));
    }
}
