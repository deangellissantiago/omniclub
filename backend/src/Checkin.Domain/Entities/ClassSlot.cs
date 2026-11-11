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

    // ---- TotalPass (ocorrência avulsa — POST /partner/event-occurrence) ----------------

    /// <summary>Vagas oferecidas ao TotalPass neste horário (separadas das do Wellhub,
    /// <see cref="Capacity"/>). No TotalPass só podem aumentar depois de publicadas.</summary>
    public int TotalPassCapacity { get; set; }

    /// <summary>Reservas TotalPass ativas — contadas a partir do webhook de reservas.</summary>
    public int TotalPassBookedCount { get; set; }

    /// <summary>Id do evento no TotalPass (vem em <c>event.id</c> no webhook de reserva — é por ele
    /// que a reserva é ligada a este horário).</summary>
    public string? TotalPassEventId { get; set; }

    /// <summary>occurrenceUuid no TotalPass. Nulo = ainda não publicado lá.</summary>
    public string? TotalPassOccurrenceUuid { get; set; }

    public bool Active { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public int AvailableSpots => Math.Max(0, Capacity - BookedCount);
}
