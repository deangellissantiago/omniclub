namespace Checkin.Application.Ports.Integrations;

/// <summary>
/// Porta de saída para a API de reservas do TotalPass ("Booking - New", host
/// booking-api.totalpass.com — autenticação própria, mesmas chaves do check-in). Usamos o modelo
/// de <b>ocorrência avulsa</b> (POST /partner/event-occurrence): um horário do OmniClub = uma
/// ocorrência lá, igual ao "um horário de cada vez" da agenda do Wellhub.
/// </summary>
public interface ITotalPassBookingGateway
{
    bool IsConfigured { get; }

    /// <summary>GET /partner/plans — planos da unidade (o planId é obrigatório pra criar aula).</summary>
    Task<IReadOnlyList<TotalPassPlan>> ListPlansAsync(string placeApiKey, CancellationToken ct = default);

    /// <summary>POST /partner/event-occurrence. Nulo se o TotalPass recusar/falhar (o horário fica
    /// pendente e sobe de novo pelo "Sincronizar") — o motivo vai para o log.</summary>
    Task<TotalPassOccurrence?> CreateOccurrenceAsync(string placeApiKey, TotalPassOccurrenceRequest request, CancellationToken ct = default);

    /// <summary>POST /partner/webhook/subscribe — URL (no endereço público do OmniClub) que recebe
    /// as reservas da unidade. Lança <see cref="TotalPassException"/> se falhar.</summary>
    Task SubscribeBookingWebhookAsync(string placeApiKey, string webhookPath, CancellationToken ct = default);
}

public record TotalPassPlan(long Id, string Name, string? Code);

/// <param name="StartsAtLocal">Início no horário de São Paulo (o TotalPass recebe data e hora
/// locais, com timezone "pt-BR").</param>
/// <param name="ExternalReference">Id do horário no OmniClub (ClassSlot.Id).</param>
public record TotalPassOccurrenceRequest(
    string Title, string Responsible, string? Description, int DurationMinutes, int Slots, long PlanId,
    DateTime StartsAtLocal, string ExternalReference);

public record TotalPassOccurrence(string EventId, string OccurrenceUuid);
