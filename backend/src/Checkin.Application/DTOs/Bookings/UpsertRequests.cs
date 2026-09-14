namespace Checkin.Application.DTOs.Bookings;

/// <summary>"Configuração de Grade: Criação de Categorias" — ver BookingService.CreateClassAsync.
/// <c>ProductId</c> é obrigatório pelo Wellhub — liste os produtos válidos da unidade primeiro em
/// <c>GET /api/classes/products/{checkinPointId}</c>.</summary>
public record CreateClassRequest(string CheckinPointId, string Name, string? Description, long ProductId);

/// <summary>"Configuração de Grade: Aulas/Slots" — ver BookingService.CreateSlotAsync.</summary>
public record CreateSlotRequest(string ClassId, DateTime StartsAt, DateTime EndsAt, int Capacity);
