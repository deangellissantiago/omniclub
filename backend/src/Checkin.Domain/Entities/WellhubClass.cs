namespace Checkin.Domain.Entities;

/// <summary>
/// Categoria de aula (ex.: "Tênis Iniciante") sincronizada com o Wellhub via Booking API —
/// "Configuração de Grade: Criação de Categorias" no fluxo descrito pelo Wellhub Technical
/// Sales. Cada categoria pertence a um ponto de check-in (a unidade onde a aula acontece).
/// </summary>
public class WellhubClass
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = string.Empty;
    public string CheckinPointId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Id do produto Wellhub (plano/tipo de acesso) ao qual esta categoria está associada
    /// — obrigatório pela API deles (POST /booking/v1/gyms/:gym_id/classes exige product_id).
    /// Consulte os produtos válidos de uma unidade via IWellhubBookingGateway.ListProductsAsync
    /// (GET /setup/v1/gyms/:gym_id/products) antes de criar a categoria.</summary>
    public long ProductId { get; set; }

    /// <summary>Id da categoria no Wellhub, preenchido depois que IWellhubBookingGateway.CreateClassAsync
    /// confirma a criação do lado deles. Nulo enquanto a sincronização não aconteceu/falhou.</summary>
    public string? ExternalId { get; set; }

    public bool Active { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
