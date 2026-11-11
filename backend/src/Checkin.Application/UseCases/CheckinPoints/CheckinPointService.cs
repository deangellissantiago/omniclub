using Checkin.Application.DTOs.Bookings;
using Checkin.Application.DTOs.CheckinPoints;
using Checkin.Application.Exceptions;
using Checkin.Application.Ports;
using Checkin.Application.Ports.Integrations;
using Checkin.Application.Ports.Repositories;
using Checkin.Domain.Entities;
using Checkin.Domain.Enums;

namespace Checkin.Application.UseCases.CheckinPoints;

public class CheckinPointService
{
    private readonly ICheckinPointRepository _repository;
    private readonly IWellhubBookingGateway _wellhubBookingGateway;
    private readonly ICurrentTenantContext _tenantContext;
    private readonly ITotalPassGateway? _totalPassGateway;

    public CheckinPointService(
        ICheckinPointRepository repository, IWellhubBookingGateway wellhubBookingGateway, ICurrentTenantContext tenantContext,
        ITotalPassGateway? totalPassGateway = null)
    {
        _repository = repository;
        _wellhubBookingGateway = wellhubBookingGateway;
        _tenantContext = tenantContext;
        _totalPassGateway = totalPassGateway;
    }

    public const string TotalPassWebhookPathPrefix = "/api/integrations/totalpass/checkins/";

    public async Task<IReadOnlyList<CheckinPointDto>> ListAsync(CancellationToken ct = default)
    {
        var points = await _repository.ListAsync(_tenantContext.TenantId, ct);
        return points.Select(ToDto).ToList();
    }

    public async Task<CheckinPointDto> CreateAsync(UpsertCheckinPointRequest request, CancellationToken ct = default)
    {
        // TotalPass: o identificador é preenchido sozinho na conexão (id da unidade lá), então
        // pode chegar vazio aqui — sem checagem de duplicidade nesse caso.
        if (!string.IsNullOrWhiteSpace(request.ExternalId))
        {
            var existing = await _repository.GetByExternalIdAsync(request.App, request.ExternalId, ct);
            if (existing is not null)
            {
                throw new ConflictException("Já existe um ponto de check-in cadastrado com este identificador para este aplicativo.");
            }
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
        ApplyTotalPassKey(point, request.TotalPassPlaceApiKey);
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
        ApplyTotalPassKey(point, request.TotalPassPlaceApiKey);

        await _repository.UpdateAsync(point, ct);
        return ToDto(point);
    }

    /// <summary>
    /// "Conectar ao TotalPass": autentica a unidade com a place_api_key dela + a chave de
    /// integrador do OmniClub e cadastra no TotalPass a URL de webhook desta unidade. Pode ser
    /// repetido sem efeito colateral (atualiza a URL em vez de duplicar). Preenche o identificador
    /// com o id da unidade no TotalPass e guarda o nome dela, pra conferência.
    /// </summary>
    public async Task<CheckinPointDto> ConnectTotalPassAsync(string id, CancellationToken ct = default)
    {
        var point = await _repository.GetByIdAsync(_tenantContext.TenantId, id, ct)
            ?? throw new NotFoundException("Ponto de check-in não encontrado.");
        if (point.App != IntegrationApp.TotalPass)
            throw new ArgumentException("Este ponto de check-in não é do TotalPass.");
        if (string.IsNullOrWhiteSpace(point.TotalPassPlaceApiKey))
            throw new ArgumentException("Informe a chave da unidade no TotalPass (place_api_key) antes de conectar.");
        if (_totalPassGateway is null || !_totalPassGateway.IsConfigured)
            throw new ConflictException("Integração TotalPass não configurada no servidor (falta a chave de integrador do OmniClub).");

        point.TotalPassWebhookKey ??= NewWebhookKey();

        TotalPassConnection connection;
        try
        {
            connection = await _totalPassGateway.ConnectPlaceAsync(point.TotalPassPlaceApiKey, TotalPassWebhookPathPrefix + point.TotalPassWebhookKey, ct);
        }
        catch (TotalPassException ex)
        {
            throw new ConflictException(ex.Message);
        }

        point.TotalPassConnectedAt = DateTime.UtcNow;
        point.TotalPassPlaceName = connection.PlaceName;
        if (!string.IsNullOrWhiteSpace(connection.PlaceId)) point.ExternalId = connection.PlaceId;
        if (string.IsNullOrWhiteSpace(point.Name) && connection.PlaceName is not null) point.Name = connection.PlaceName;

        await _repository.UpdateAsync(point, ct);
        return ToDto(point);
    }

    /// <summary>Chave nova (ou trocada) → precisa reconectar: a conexão anterior foi feita com a
    /// chave antiga. Também garante a chave secreta da URL de webhook.</summary>
    private static void ApplyTotalPassKey(CheckinPoint point, string? placeApiKey)
    {
        if (point.App != IntegrationApp.TotalPass) return;
        point.TotalPassWebhookKey ??= NewWebhookKey();

        var key = placeApiKey?.Trim();
        if (string.IsNullOrEmpty(key) || key == point.TotalPassPlaceApiKey) return;
        point.TotalPassPlaceApiKey = key;
        point.TotalPassConnectedAt = null;
    }

    private static string NewWebhookKey() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

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
        p.PricePerCheckinCents,
        p.App == IntegrationApp.TotalPass
            ? new TotalPassStatusDto(
                !string.IsNullOrWhiteSpace(p.TotalPassPlaceApiKey),
                p.TotalPassPlaceApiKey is { Length: > 4 } k ? "…" + k[^4..] : null,
                p.TotalPassConnectedAt,
                p.TotalPassPlaceName)
            : null);
}
