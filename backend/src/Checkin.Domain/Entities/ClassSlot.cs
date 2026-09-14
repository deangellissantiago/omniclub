namespace Checkin.Domain.Entities;

/// <summary>
/// Uma aula agendada (horário + vagas) de uma <see cref="WellhubClass"/> — "Aulas/Slots" no
/// fluxo do Wellhub Technical Sales. <see cref="BookedCount"/> é mantido em sincronia com o
/// Wellhub via PATCH Update ("Atualização de Vagas") sempre que uma reserva é
/// confirmada/cancelada, aqui ou em outro sistema.
/// </summary>
public class ClassSlot
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = string.Empty;
    public string ClassId { get; set; } = string.Empty;

    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public int Capacity { get; set; }
    public int BookedCount { get; set; }

    /// <summary>Id do slot no Wellhub, preenchido depois que IWellhubBookingGateway.CreateSlotAsync
    /// confirma a criação do lado deles.</summary>
    public string? ExternalId { get; set; }

    public bool Active { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public int AvailableSpots => Math.Max(0, Capacity - BookedCount);
}
