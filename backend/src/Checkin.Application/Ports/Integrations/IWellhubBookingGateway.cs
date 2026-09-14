using Checkin.Application.DTOs.Bookings;

namespace Checkin.Application.Ports.Integrations;

/// <summary>
/// Porta de saída (outbound) para a Booking API do Wellhub — "Configuração de Grade",
/// "Atualização de Vagas" e "Gestão de Reservas" no fluxo confirmado com o Wellhub Technical
/// Sales por e-mail.
///
/// Contrato confirmado em 2026-09-10 contra o Sandbox real, a partir da collection do Postman
/// fornecida pelo parceiro (<c>Old - Gympass Quick Start Guide - Booking &amp; Access Control API
/// Copy.postman_collection.json</c>) — ao contrário da tentativa inicial (que chutou
/// <c>/booking/v1/classes</c> sem o prefixo <c>/gyms/:gym_id/</c> e errou o nome dos campos de
/// vaga), os paths abaixo vêm direto dos requests reais da collection. Ainda não testamos uma
/// chamada de escrita de verdade (criar categoria/aula) porque isso exige um <c>product_id</c>
/// válido da unidade — use <see cref="ListProductsAsync"/> primeiro (esse sim já testado: GET
/// /setup/v1/gyms/609/products retornou 200 com os produtos reais da unidade de Sandbox).
/// </summary>
public interface IWellhubBookingGateway
{
    /// <summary>Indica se há credenciais (<c>Wellhub:ApiKey</c>) configuradas.</summary>
    bool IsConfigured { get; }

    /// <summary>GET /setup/v1/gyms/:gym_id/products — produtos (planos/tipos de acesso) válidos
    /// da unidade. Um <c>product_id</c> daqui é obrigatório para criar categorias/aulas.</summary>
    Task<IReadOnlyList<WellhubProductDto>?> ListProductsAsync(string gymExternalId, CancellationToken ct = default);

    /// <summary>POST /booking/v1/gyms/:gym_id/classes — cria uma categoria de aula no Wellhub.
    /// Retorna o id externo, ou null se falhar/não configurado.</summary>
    Task<string?> CreateClassAsync(string gymExternalId, string name, string? description, long productId, CancellationToken ct = default);

    /// <summary>POST /booking/v1/gyms/:gym_id/classes/:class_id/slots — cria uma aula/slot
    /// agendado, vinculado a uma categoria. Retorna o id externo, ou null se falhar/não configurado.</summary>
    Task<string?> CreateSlotAsync(
        string gymExternalId, string classExternalId, long productId, DateTime startsAt, DateTime endsAt, int capacity,
        CancellationToken ct = default);

    /// <summary>PATCH /booking/v1/gyms/:gym_id/classes/:class_id/slots/:slot_id — atualização
    /// parcial de vagas ("Atualização de Vagas"). Campos confirmados na collection:
    /// <c>total_capacity</c> e <c>total_booked</c> (não "available_spots", como a v1 desta
    /// integração assumia).</summary>
    Task<bool> UpdateSlotVacancyAsync(
        string gymExternalId, string classExternalId, string slotExternalId, int totalCapacity, int totalBooked,
        CancellationToken ct = default);

    /// <summary>PATCH /booking/v1/gyms/:gym_id/bookings/:booking_number — confirma uma reserva
    /// (resposta a booking-requested). Corpo confirmado na collection: <c>{"class_id", "status"}</c>,
    /// com <c>status: 2</c> = confirmado (visto no exemplo real da collection).</summary>
    Task<bool> ConfirmBookingAsync(string gymExternalId, string classExternalId, string bookingNumber, CancellationToken ct = default);

    /// <summary>PATCH /booking/v1/gyms/:gym_id/bookings/:booking_number — rejeita uma reserva
    /// (sem vaga). Mesmo endpoint do confirm; o código de status para "rejeitado" NÃO estava no
    /// exemplo da collection (só o de confirmação, <c>2</c>) — usamos <c>3</c> como melhor
    /// palpite (ver WellhubBookingGatewayAdapter). Confirme com o Wellhub antes de depender
    /// disso em produção.</summary>
    Task<bool> RejectBookingAsync(string gymExternalId, string classExternalId, string bookingNumber, CancellationToken ct = default);
}
