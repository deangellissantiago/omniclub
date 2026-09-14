using Checkin.Application.DTOs.Bookings;
using Checkin.Application.DTOs.CheckinPoints;
using Checkin.Application.Exceptions;
using Checkin.Application.Ports;
using Checkin.Application.Ports.Integrations;
using Checkin.Application.Ports.Repositories;
using Checkin.Domain.Entities;

namespace Checkin.Application.UseCases.CheckinPoints;

public class CheckinPointService
{
    private readonly ICheckinPointRepository _repository;
    private readonly IWellhubBookingGateway _wellhubBookingGateway;
    private readonly ICurrentTenantContext _tenantContext;

    public CheckinPointService(
        ICheckinPointRepository repository, IWellhubBookingGateway wellhubBookingGateway, ICurrentTenantContext tenantContext)
    {
        _repository = repository;
        _wellhubBookingGateway = wellhubBookingGateway;
        _tenantContext = tenantContext;
    }

    public async Task<IReadOnlyList<CheckinPointDto>> ListAsync(CancellationToken ct = default)
    {
        var points = await _repository.ListAsync(_tenantContext.TenantId, ct);
        return points.Select(ToDto).ToList();
    }

    public async Task<CheckinPointDto> CreateAsync(UpsertCheckinPointRequest request, CancellationToken ct = default)
    {
        var existing = await _repository.GetByExternalIdAsync(request.App, request.ExternalId, ct);
        if (existing is not null)
        {
            throw new ConflictException("Já existe um ponto de check-in cadastrado com este identificador para este aplicativo.");
        }

        var point = new CheckinPoint
        {
            TenantId = _tenantContext.TenantId,
            App = request.App,
            ExternalId = request.ExternalId,
            Name = request.Name,
            Active = request.Active,
            PricePerCheckinCents = request.PricePerCheckinCents
        };
        var created = await _repository.CreateAsync(point, ct);
        return ToDto(created);
    }

    public async Task<CheckinPointDto> UpdateAsync(string id, UpsertCheckinPointRequest request, CancellationToken ct = default)
    {
        var point = await _repository.GetByIdAsync(_tenantContext.TenantId, id, ct)
            ?? throw new NotFoundException("Ponto de check-in não encontrado.");

        point.App = request.App;
        point.ExternalId = request.ExternalId;
        point.Name = request.Name;
        point.Active = request.Active;
        point.PricePerCheckinCents = request.PricePerCheckinCents;

        await _repository.UpdateAsync(point, ct);
        return ToDto(point);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        var deleted = await _repository.DeleteAsync(_tenantContext.TenantId, id, ct);
        if (!deleted) throw new NotFoundException("Ponto de check-in não encontrado.");
    }

    /// <summary>Busca os produtos (planos/tipos de acesso) reais deste ponto no Wellhub
    /// (GET /setup/v1/gyms/:gym_id/products) e vincula ao cadastro, pra ficarem visíveis sem
    /// precisar buscar de novo toda vez — usados depois para criar categorias de aula
    /// (WellhubClass.ProductId).</summary>
    public async Task<CheckinPointDto> SyncProductsAsync(string id, CancellationToken ct = default)
    {
        var point = await _repository.GetByIdAsync(_tenantContext.TenantId, id, ct)
            ?? throw new NotFoundException("Ponto de check-in não encontrado.");

        if (!_wellhubBookingGateway.IsConfigured)
        {
            throw new ArgumentException("Integração Wellhub não configurada (Wellhub:ApiKey ausente) — não é possível buscar produtos agora.");
        }

        var products = await _wellhubBookingGateway.ListProductsAsync(point.ExternalId, ct)
            ?? throw new ArgumentException($"Não foi possível buscar os produtos do Wellhub para o ponto '{point.Name}' agora (ver logs do backend para o motivo).");

        point.Products = products.Select(p => new WellhubProductRef { ProductId = p.ProductId, Name = p.Name, Virtual = p.Virtual }).ToList();
        await _repository.UpdateAsync(point, ct);

        return ToDto(point);
    }

    private static CheckinPointDto ToDto(CheckinPoint p) => new(
        p.Id, p.App, p.ExternalId, p.Name, p.Active, p.CreatedAt,
        p.Products.Select(pr => new WellhubProductDto(pr.ProductId, pr.Name, pr.Virtual)).ToList(),
        p.PricePerCheckinCents);
}
