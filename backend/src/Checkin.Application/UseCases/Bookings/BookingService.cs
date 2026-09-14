using Checkin.Application.DTOs.Bookings;
using Checkin.Application.Ports;
using Checkin.Application.Ports.Integrations;
using Checkin.Application.Ports.Repositories;
using Checkin.Domain.Entities;
using Checkin.Domain.Enums;

namespace Checkin.Application.UseCases.Bookings;

/// <summary>
/// Casos de uso da Booking API do Wellhub: "Configuração de Grade" (categorias/slots),
/// "Atualização de Vagas" e "Gestão de Reservas" — fluxo confirmado com o Wellhub Technical
/// Sales. Reservas seguem a mesma filosofia de aprovação automática dos check-ins (ver
/// CheckinService): confirmamos na hora se há vaga, rejeitamos se não há — sem fila de espera
/// nem aprovação manual.
/// </summary>
public class BookingService
{
    private readonly IClassRepository _classes;
    private readonly IClassSlotRepository _slots;
    private readonly IBookingRepository _bookings;
    private readonly IStudentRepository _students;
    private readonly ICheckinPointRepository _checkinPoints;
    private readonly IWellhubBookingGateway _gateway;
    private readonly ICurrentTenantContext _tenantContext;

    public BookingService(
        IClassRepository classes, IClassSlotRepository slots, IBookingRepository bookings,
        IStudentRepository students, ICheckinPointRepository checkinPoints,
        IWellhubBookingGateway gateway, ICurrentTenantContext tenantContext)
    {
        _classes = classes;
        _slots = slots;
        _bookings = bookings;
        _students = students;
        _checkinPoints = checkinPoints;
        _gateway = gateway;
        _tenantContext = tenantContext;
    }

    // ---- Configuração de Grade -------------------------------------------------------------

    /// <summary>Produtos (planos/tipos de acesso) válidos da unidade — um <c>ProductId</c> daqui
    /// é obrigatório para criar uma categoria (ver <see cref="CreateClassAsync"/>).</summary>
    public async Task<IReadOnlyList<WellhubProductDto>> ListProductsAsync(string checkinPointId, CancellationToken ct = default)
    {
        var checkinPoint = await _checkinPoints.GetByIdAsync(_tenantContext.TenantId, checkinPointId, ct)
            ?? throw new Exceptions.NotFoundException($"Ponto de check-in '{checkinPointId}' não encontrado.");

        if (!_gateway.IsConfigured) return Array.Empty<WellhubProductDto>();

        return await _gateway.ListProductsAsync(checkinPoint.ExternalId, ct) ?? Array.Empty<WellhubProductDto>();
    }

    public async Task<ClassDto> CreateClassAsync(CreateClassRequest request, CancellationToken ct = default)
    {
        var checkinPoint = await _checkinPoints.GetByIdAsync(_tenantContext.TenantId, request.CheckinPointId, ct)
            ?? throw new Exceptions.NotFoundException($"Ponto de check-in '{request.CheckinPointId}' não encontrado.");

        var externalId = _gateway.IsConfigured
            ? await _gateway.CreateClassAsync(checkinPoint.ExternalId, request.Name, request.Description, request.ProductId, ct)
            : null;

        var wellhubClass = new WellhubClass
        {
            TenantId = _tenantContext.TenantId,
            CheckinPointId = checkinPoint.Id,
            Name = request.Name,
            Description = request.Description,
            ProductId = request.ProductId,
            ExternalId = externalId
        };

        var created = await _classes.CreateAsync(wellhubClass, ct);
        return ToClassDto(created);
    }

    public async Task<ClassSlotDto> CreateSlotAsync(CreateSlotRequest request, CancellationToken ct = default)
    {
        var wellhubClass = await _classes.GetByIdAsync(_tenantContext.TenantId, request.ClassId, ct)
            ?? throw new Exceptions.NotFoundException($"Categoria de aula '{request.ClassId}' não encontrada.");

        string? externalId = null;
        if (_gateway.IsConfigured && wellhubClass.ExternalId is not null)
        {
            var checkinPoint = await _checkinPoints.GetByIdAsync(_tenantContext.TenantId, wellhubClass.CheckinPointId, ct);
            if (checkinPoint is not null)
            {
                externalId = await _gateway.CreateSlotAsync(
                    checkinPoint.ExternalId, wellhubClass.ExternalId, wellhubClass.ProductId,
                    request.StartsAt, request.EndsAt, request.Capacity, ct);
            }
        }

        var slot = new ClassSlot
        {
            TenantId = _tenantContext.TenantId,
            ClassId = wellhubClass.Id,
            StartsAt = request.StartsAt,
            EndsAt = request.EndsAt,
            Capacity = request.Capacity,
            ExternalId = externalId
        };

        var created = await _slots.CreateAsync(slot, ct);
        return ToSlotDto(created);
    }

    // ---- Gestão de Reservas (webhook) --------------------------------------------------------

    /// <summary>Evento <c>booking-requested</c>: confirma na hora se há vaga, senão rejeita.</summary>
    public async Task<BookingDto> HandleBookingRequestedAsync(
        string slotExternalId, string gympassId, string externalBookingId, DateTime requestedAt, string? rawPayload,
        CancellationToken ct = default)
    {
        var slot = await _slots.GetByExternalIdAsync(slotExternalId, ct)
            ?? throw new Exceptions.NotFoundException($"Nenhuma aula/slot cadastrado para o id '{slotExternalId}'.");

        var existing = await _bookings.FindByExternalBookingIdAsync(slot.TenantId, externalBookingId, ct);
        if (existing is not null)
        {
            var existingStudent = existing.StudentId is not null ? await _students.GetByIdAsync(slot.TenantId, existing.StudentId, ct) : null;
            return ToBookingDto(existing, existingStudent);
        }

        var student = await _students.GetByWellhubMemberIdAsync(gympassId, ct);
        var wellhubClass = await _classes.GetByIdAsync(slot.TenantId, slot.ClassId, ct);
        var checkinPoint = wellhubClass is null ? null : await _checkinPoints.GetByIdAsync(slot.TenantId, wellhubClass.CheckinPointId, ct);

        var booking = new Booking
        {
            TenantId = slot.TenantId,
            SlotId = slot.Id,
            StudentId = student?.Id,
            GympassId = gympassId,
            ExternalBookingId = externalBookingId,
            RequestedAt = requestedAt,
            RawPayload = rawPayload
        };

        if (slot.AvailableSpots > 0)
        {
            booking.Status = BookingStatus.Confirmed;
            slot.BookedCount++;
            await _slots.UpdateAsync(slot, ct);

            if (_gateway.IsConfigured && checkinPoint is not null && wellhubClass?.ExternalId is not null)
            {
                await _gateway.ConfirmBookingAsync(checkinPoint.ExternalId, wellhubClass.ExternalId, externalBookingId, ct);
                await SyncVacancyAsync(checkinPoint, wellhubClass, slot, ct);
            }
        }
        else
        {
            booking.Status = BookingStatus.Rejected;
            if (_gateway.IsConfigured && checkinPoint is not null && wellhubClass?.ExternalId is not null)
            {
                await _gateway.RejectBookingAsync(checkinPoint.ExternalId, wellhubClass.ExternalId, externalBookingId, ct);
            }
        }

        booking.RespondedAt = DateTime.UtcNow;
        var created = await _bookings.CreateAsync(booking, ct);
        return ToBookingDto(created, student);
    }

    /// <summary>Eventos <c>booking-canceled</c> / <c>booking-late-canceled</c>: libera a vaga se a
    /// reserva estava confirmada. Retorna null se a reserva é desconhecida (nunca vimos o
    /// booking-requested correspondente) — nada a fazer do nosso lado além de confirmar recebimento.</summary>
    public async Task<BookingDto?> HandleBookingCanceledAsync(
        string slotExternalId, string externalBookingId, bool lateCancel, CancellationToken ct = default)
    {
        var slot = await _slots.GetByExternalIdAsync(slotExternalId, ct)
            ?? throw new Exceptions.NotFoundException($"Nenhuma aula/slot cadastrado para o id '{slotExternalId}'.");

        var booking = await _bookings.FindByExternalBookingIdAsync(slot.TenantId, externalBookingId, ct);
        if (booking is null) return null;

        if (booking.Status is BookingStatus.Canceled or BookingStatus.LateCanceled)
        {
            var alreadyStudent = booking.StudentId is not null ? await _students.GetByIdAsync(slot.TenantId, booking.StudentId, ct) : null;
            return ToBookingDto(booking, alreadyStudent); // idempotência: reenvio do mesmo evento
        }

        var wasConfirmed = booking.Status == BookingStatus.Confirmed;
        booking.Status = lateCancel ? BookingStatus.LateCanceled : BookingStatus.Canceled;
        booking.RespondedAt = DateTime.UtcNow;
        await _bookings.UpdateAsync(booking, ct);

        if (wasConfirmed)
        {
            slot.BookedCount = Math.Max(0, slot.BookedCount - 1);
            await _slots.UpdateAsync(slot, ct);

            if (_gateway.IsConfigured)
            {
                var wellhubClass = await _classes.GetByIdAsync(slot.TenantId, slot.ClassId, ct);
                var checkinPoint = wellhubClass is null ? null : await _checkinPoints.GetByIdAsync(slot.TenantId, wellhubClass.CheckinPointId, ct);
                await SyncVacancyAsync(checkinPoint, wellhubClass, slot, ct);
            }
        }

        var student = booking.StudentId is not null ? await _students.GetByIdAsync(slot.TenantId, booking.StudentId, ct) : null;
        return ToBookingDto(booking, student);
    }

    private async Task SyncVacancyAsync(CheckinPoint? checkinPoint, WellhubClass? wellhubClass, ClassSlot slot, CancellationToken ct)
    {
        if (checkinPoint is null || wellhubClass?.ExternalId is null || slot.ExternalId is null) return;
        await _gateway.UpdateSlotVacancyAsync(checkinPoint.ExternalId, wellhubClass.ExternalId, slot.ExternalId, slot.Capacity, slot.BookedCount, ct);
    }

    private static ClassDto ToClassDto(WellhubClass c) => new(c.Id, c.CheckinPointId, c.Name, c.Description, c.ProductId, c.ExternalId, c.Active);

    private static ClassSlotDto ToSlotDto(ClassSlot s) => new(s.Id, s.ClassId, s.StartsAt, s.EndsAt, s.Capacity, s.BookedCount, s.ExternalId);

    private static BookingDto ToBookingDto(Booking b, Student? student) =>
        new(b.Id, b.SlotId, b.StudentId, student?.Name, b.GympassId, b.Status, b.RequestedAt, b.RespondedAt);
}
