using Checkin.Domain.Enums;

namespace Checkin.Domain.Entities;

/// <summary>
/// Ponto de check-in (quadra/unidade) cadastrado pelo admin do tenant, vinculado a um app
/// (ex.: o "gym id" do Wellhub, como 500977 = Dynamis Santa Lúcia).
/// </summary>
public class CheckinPoint
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = string.Empty;
    public IntegrationApp App { get; set; }

    /// <summary>Identificador do ponto no app de origem (ex.: Wellhub GymId "500977").</summary>
    public string ExternalId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public bool Active { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Produtos Wellhub vinculados a este ponto (ver CheckinPointService.SyncProductsAsync
    /// e WellhubProductRef). Vazio até alguém clicar em "Buscar produtos" no cadastro do ponto.</summary>
    public List<WellhubProductRef> Products { get; set; } = new();
}
