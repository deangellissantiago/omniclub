namespace Checkin.Domain.Entities;

/// <summary>
/// Cópia local de um produto (plano/tipo de acesso) do Wellhub, vinculado a um
/// <see cref="CheckinPoint"/> via <see cref="CheckinPointService.SyncProductsAsync"/>
/// (GET /setup/v1/gyms/:gym_id/products) — pra ficar visível no cadastro do ponto sem precisar
/// buscar no Wellhub toda hora. Um <c>ProductId</c> daqui é o que se usa para criar categorias de
/// aula (ver WellhubClass.ProductId).
/// </summary>
public class WellhubProductRef
{
    public long ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Virtual { get; set; }
}
