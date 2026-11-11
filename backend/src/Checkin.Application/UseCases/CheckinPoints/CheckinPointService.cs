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
        var point = new CheckinPoint { TenantId = _tenantContext.TenantId };
        await ApplyAsync(point, request, ct);
        var created = await _repository.CreateAsync(point, ct);
        return ToDto(created);
    }

    public async Task<CheckinPointDto> UpdateAsync(string id, UpsertCheckinPointRequest request, CancellationToken ct = default)
    {
        var point = await _repository.GetByIdAsync(_tenantContext.TenantId, id, ct)
            ?? throw new NotFoundException("Ponto de check-in não encontrado.");

        await ApplyAsync(point, request, ct);
        await _repository.UpdateAsync(point, ct);
        return ToDto(point);
    }

    /// <summary>Aplica o formulário na unidade: dados gerais + cada integração (Wellhub pelo Gym
    /// ID, TotalPass pela chave da unidade), validando que pelo menos uma fica ativa.</summary>
    private async Task ApplyAsync(CheckinPoint point, UpsertCheckinPointRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Informe o nome da unidade.");

        var gymId = request.ExternalId?.Trim() ?? string.Empty;
        if (gymId.Length > 0 && gymId != point.ExternalId)
        {
            var existing = await _repository.GetByWellhubGymIdAsync(gymId, ct);
            if (existing is not null && existing.Id != point.Id)
                throw new ConflictException("Já existe uma unidade cadastrada com este Gym ID do Wellhub.");
        }

        var placeKey = request.TotalPassPlaceApiKey?.Trim();
        if (request.TotalPassEnabled && string.IsNullOrEmpty(placeKey) && string.IsNullOrWhiteSpace(point.TotalPassPlaceApiKey))
            throw new ArgumentException("Informe a chave da unidade no TotalPass (place_api_key).");
        if (gymId.Length == 0 && !request.TotalPassEnabled)
            throw new ArgumentException("Ative pelo menos um app: informe o Gym ID do Wellhub e/ou a chave do TotalPass.");

        point.Name = request.Name.Trim();
        point.Active = request.Active;
        point.ExternalId = gymId;
        point.PricePerCheckinCents = gymId.Length > 0 ? request.PricePerCheckinCents : null;
        if (gymId.Length == 0) point.Products = new();

        if (!request.TotalPassEnabled)
        {
            // Desligar o TotalPass: sem chave da URL, o webhook cadastrado lá passa a receber 404.
            point.TotalPassPlaceApiKey = null;
            point.TotalPassWebhookKey = null;
            point.TotalPassConnectedAt = null;
            point.TotalPassPlaceName = null;
            point.TotalPassPlaceId = null;
            point.TotalPassPricePerCheckinCents = null;
            return;
        }

        point.TotalPassPricePerCheckinCents = request.TotalPassPricePerCheckinCents;
        point.TotalPassWebhookKey ??= NewWebhookKey();
        if (!string.IsNullOrEmpty(placeKey) && placeKey != point.TotalPassPlaceApiKey)
        {
            // Chave nova (ou trocada) → precisa reconectar: a conexão anterior era da chave antiga.
            point.TotalPassPlaceApiKey = placeKey;
            point.TotalPassConnectedAt = null;
        }
    }

    /// <summary>
    /// "Conectar ao TotalPass": autentica a unidade com a place_api_key dela + a chave de
    /// integrador do OmniClub e cadastra no TotalPass a URL de webhook desta unidade. Pode ser
    /// repetido sem efeito colateral (atualiza a URL em vez de duplicar). Guarda o id e o nome da
    /// unidade no TotalPass, pra conferência.
    /// </summary>
    public async Task<CheckinPointDto> ConnectTotalPassAsync(string id, CancellationToken ct = default)
    {
        var point = await _repository.GetByIdAsync(_tenantContext.TenantId, id, ct)
            ?? throw new NotFoundException("Ponto de check-in não encontrado.");
        if (!point.AcceptsTotalPass())
            throw new ArgumentException("Ative o TotalPass nesta unidade e informe a chave dela (place_api_key) antes de conectar.");
        if (_totalPassGateway is null || !_totalPassGateway.IsConfigured)
            throw new ConflictException("Integração TotalPass não configurada no servidor (falta a chave de integrador do OmniClub).");

        point.TotalPassWebhookKey ??= NewWebhookKey();

        TotalPassConnection connection;
        try
        {
            connection = await _totalPassGateway.ConnectPlaceAsync(point.TotalPassPlaceApiKey!, TotalPassWebhookPathPrefix + point.TotalPassWebhookKey, ct);
        }
        catch (TotalPassException ex)
        {
            throw new ConflictException(ex.Message);
        }

        point.TotalPassConnectedAt = DateTime.UtcNow;
        point.TotalPassPlaceName = connection.PlaceName;
        if (!string.IsNullOrWhiteSpace(connection.PlaceId)) point.TotalPassPlaceId = connection.PlaceId;

        await _repository.UpdateAsync(point, ct);
        return ToDto(point);
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

        if (!point.AcceptsWellhub())
            throw new ArgumentException("Esta unidade não tem Gym ID do Wellhub — informe-o antes de buscar produtos.");

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
        p.Id, p.Name, p.Active, p.CreatedAt, p.Apps(),
        p.ExternalId,
        p.Products.Select(pr => new WellhubProductDto(pr.ProductId, pr.Name, pr.Virtual)).ToList(),
        p.PricePerCheckinCents,
        p.AcceptsTotalPass()
            ? new TotalPassStatusDto(
                true,
                p.TotalPassPlaceApiKey is { Length: > 4 } k ? "…" + k[^4..] : null,
                p.TotalPassConnectedAt,
                p.TotalPassPlaceName,
                p.TotalPassPlaceId,
                p.TotalPassPricePerCheckinCents)
            : null);
}
