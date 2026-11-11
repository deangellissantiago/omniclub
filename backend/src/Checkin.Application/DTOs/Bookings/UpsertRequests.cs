namespace Checkin.Application.DTOs.Bookings;

/// <summary>"Configuração de Grade: Criação de Categorias" — ver BookingService.CreateClassAsync.
/// <c>ProductId</c> é obrigatório pelo Wellhub — liste os produtos válidos da unidade primeiro em
/// <c>GET /api/classes/products/{checkinPointId}</c>.
/// Publicação por app: Wellhub precisa de <c>ProductId</c>; TotalPass precisa de
/// <c>TotalPassPlanId</c> (planos em <c>GET /api/classes/totalpass-plans/{checkinPointId}</c>) e de
/// um responsável (<c>Instructor</c>).</summary>
public record CreateClassRequest(
    string CheckinPointId, string Name, string? Description, long? ProductId,
    bool PublishWellhub = true, bool PublishTotalPass = false, long? TotalPassPlanId = null, string? Instructor = null);

/// <summary>"Configuração de Grade: Aulas/Slots" — ver BookingService.CreateSlotAsync.
/// <c>Capacity</c> = vagas Wellhub; <c>TotalPassCapacity</c> = vagas TotalPass (cada app tem as
/// suas).</summary>
public record CreateSlotRequest(string ClassId, DateTime StartsAt, DateTime EndsAt, int Capacity, int TotalPassCapacity = 0);
