using Checkin.Application.Common;
using Checkin.Application.DTOs.Bookings;
using Checkin.Application.DTOs.Checkins;
using Checkin.Application.Ports;
using Checkin.Application.Ports.Integrations;
using Checkin.Application.Ports.Repositories;
using Checkin.Application.UseCases.Students;
using Checkin.Domain.Entities;
using Checkin.Domain.Enums;

namespace Checkin.Application.UseCases.Bookings;

/// <summary>
/// Agenda e reservas dos apps. <b>Wellhub</b> (Booking API): "Configuração de Grade"
/// (categorias/slots), "Atualização de Vagas" e "Gestão de Reservas" — confirmamos na hora se há
/// vaga, rejeitamos se não há. <b>TotalPass</b> ("Booking - New"): cada horário vira uma ocorrência
/// avulsa lá; o próprio TotalPass controla a lotação e confirma a reserva (hasSlotConfirmation
/// fica no padrão, false) — aqui só registramos o que chega pelo webhook de reservas.
///
/// Uma aula (WellhubClass — nome mantido por compatibilidade) pode ser publicada nos dois apps
/// que a unidade aceita, com vagas separadas por app.
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
    private readonly ITotalPassBookingGateway? _totalPass;

    /// <param name="totalPass">Opcional só pra não obrigar os testes do Wellhub a montarem o
    /// TotalPass — em produção vem sempre do DI.</param>
    public BookingService(
        IClassRepository classes, IClassSlotRepository slots, IBookingRepository bookings,
        IStudentRepository students, ICheckinPointRepository checkinPoints,
        IWellhubBookingGateway gateway, ICurrentTenantContext tenantContext,
        ITotalPassBookingGateway? totalPass = null)
    {
        _classes = classes;
        _slots = slots;
        _bookings = bookings;
        _students = students;
        _checkinPoints = checkinPoints;
        _gateway = gateway;
        _tenantContext = tenantContext;
        _totalPass = totalPass;
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

    /// <summary>Planos TotalPass da unidade (o planId é obrigatório pra publicar aula lá).</summary>
    public async Task<IReadOnlyList<TotalPassPlanDto>> ListTotalPassPlansAsync(string checkinPointId, CancellationToken ct = default)
    {
        var point = await GetPointAsync(checkinPointId, ct);
        if (!point.AcceptsTotalPass())
            throw new ArgumentException("Esta unidade não aceita TotalPass.");
        EnsureTotalPassConfigured();

        try
        {
            var plans = await _totalPass!.ListPlansAsync(point.TotalPassPlaceApiKey!, ct);
            return plans.Select(p => new TotalPassPlanDto(p.Id, p.Name, p.Code)).ToList();
        }
        catch (TotalPassException ex)
        {
            throw new Exceptions.ConflictException(ex.Message);
        }
    }

    public async Task<ClassDto> CreateClassAsync(CreateClassRequest request, CancellationToken ct = default)
    {
        var checkinPoint = await GetPointAsync(request.CheckinPointId, ct);

        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Informe o nome da aula.");
        if (!request.PublishWellhub && !request.PublishTotalPass)
            throw new ArgumentException("Escolha em qual app publicar a aula: Wellhub e/ou TotalPass.");
        if (request.PublishWellhub)
        {
            if (!checkinPoint.AcceptsWellhub()) throw new ArgumentException("Esta unidade não aceita Wellhub.");
            if (request.ProductId is not > 0) throw new ArgumentException("Escolha o produto Wellhub da aula.");
        }
        if (request.PublishTotalPass)
        {
            if (!checkinPoint.AcceptsTotalPass()) throw new ArgumentException("Esta unidade não aceita TotalPass.");
            if (request.TotalPassPlanId is not > 0) throw new ArgumentException("Escolha o plano TotalPass da aula.");
        }

        var externalId = request.PublishWellhub && _gateway.IsConfigured
            ? await _gateway.CreateClassAsync(checkinPoint.ExternalId, request.Name, request.Description, request.ProductId!.Value, ct)
            : null;

        var wellhubClass = new WellhubClass
        {
            TenantId = _tenantContext.TenantId,
            CheckinPointId = checkinPoint.Id,
            Name = request.Name.Trim(),
            Description = request.Description,
            ProductId = request.PublishWellhub ? request.ProductId!.Value : 0,
            ExternalId = externalId,
            PublishWellhub = request.PublishWellhub,
            PublishTotalPass = request.PublishTotalPass,
            TotalPassPlanId = request.PublishTotalPass ? request.TotalPassPlanId : null,
            Instructor = string.IsNullOrWhiteSpace(request.Instructor) ? null : request.Instructor.Trim(),
        };

        var created = await _classes.CreateAsync(wellhubClass, ct);
        return ToClassDto(created);
    }

    public async Task<IReadOnlyList<ClassDto>> ListClassesAsync(CancellationToken ct = default) =>
        (await _classes.ListAsync(_tenantContext.TenantId, ct)).Select(ToClassDto).ToList();

    public async Task<IReadOnlyList<ClassSlotDto>> ListSlotsAsync(string classId, CancellationToken ct = default)
    {
        _ = await _classes.GetByIdAsync(_tenantContext.TenantId, classId, ct)
            ?? throw new Exceptions.NotFoundException($"Categoria de aula '{classId}' não encontrada.");

        return (await _slots.ListByClassAsync(_tenantContext.TenantId, classId, ct)).Select(ToSlotDto).ToList();
    }

    /// <summary>
    /// Reenvia aos apps o que ficou só do nosso lado: no Wellhub, a categoria (se o POST original
    /// falhou ou foi feito sem Wellhub:ApiKey) e os horários futuros sem id externo; no TotalPass,
    /// os horários futuros ainda não publicados. Horários passados ficam de fora — não faz sentido
    /// publicar pro aluno reservar uma aula que já aconteceu.
    /// </summary>
    public async Task<ClassDto> SyncClassAsync(string classId, CancellationToken ct = default)
    {
        var wellhubClass = await _classes.GetByIdAsync(_tenantContext.TenantId, classId, ct)
            ?? throw new Exceptions.NotFoundException($"Categoria de aula '{classId}' não encontrada.");

        if (wellhubClass.PublishWellhub && !_gateway.IsConfigured)
            throw new Exceptions.ConflictException("Integração com o Wellhub não configurada — não há como sincronizar agora.");
        if (wellhubClass.PublishTotalPass) EnsureTotalPassConfigured();

        var checkinPoint = await GetPointAsync(wellhubClass.CheckinPointId, ct);

        if (wellhubClass.PublishWellhub && wellhubClass.ExternalId is null)
        {
            wellhubClass.ExternalId = await _gateway.CreateClassAsync(
                checkinPoint.ExternalId, wellhubClass.Name, wellhubClass.Description, wellhubClass.ProductId, ct)
                ?? throw new Exceptions.ConflictException("O Wellhub recusou a criação da categoria — confira o produto escolhido e tente de novo.");
            wellhubClass.UpdatedAt = DateTime.UtcNow;
            await _classes.UpdateAsync(wellhubClass, ct);
        }

        var futureSlots = (await _slots.ListByClassAsync(_tenantContext.TenantId, wellhubClass.Id, ct))
            .Where(s => s.StartsAt > DateTime.UtcNow);

        foreach (var slot in futureSlots)
        {
            var changed = false;
            if (wellhubClass.PublishWellhub && slot.ExternalId is null && slot.Capacity > 0)
            {
                slot.ExternalId = await _gateway.CreateSlotAsync(
                    checkinPoint.ExternalId, wellhubClass.ExternalId!, wellhubClass.ProductId,
                    slot.StartsAt, slot.EndsAt, slot.Capacity, ct);
                changed |= slot.ExternalId is not null;
            }
            if (wellhubClass.PublishTotalPass && slot.TotalPassOccurrenceUuid is null && slot.TotalPassCapacity > 0)
            {
                changed |= await PublishToTotalPassAsync(checkinPoint, wellhubClass, slot, ct);
            }
            if (!changed) continue;

            slot.UpdatedAt = DateTime.UtcNow;
            await _slots.UpdateAsync(slot, ct);
        }

        return ToClassDto(wellhubClass);
    }

    public async Task<ClassSlotDto> CreateSlotAsync(CreateSlotRequest request, CancellationToken ct = default)
    {
        if (request.EndsAt <= request.StartsAt)
            throw new ArgumentException("O horário de término precisa ser depois do início.");

        var wellhubClass = await _classes.GetByIdAsync(_tenantContext.TenantId, request.ClassId, ct)
            ?? throw new Exceptions.NotFoundException($"Categoria de aula '{request.ClassId}' não encontrada.");

        if (wellhubClass.PublishWellhub && request.Capacity <= 0)
            throw new ArgumentException("Informe pelo menos 1 vaga para o Wellhub.");
        if (wellhubClass.PublishTotalPass && request.TotalPassCapacity <= 0)
            throw new ArgumentException("Informe pelo menos 1 vaga para o TotalPass.");

        var checkinPoint = await _checkinPoints.GetByIdAsync(_tenantContext.TenantId, wellhubClass.CheckinPointId, ct);

        // O objeto nasce antes das chamadas: o Id dele vai como externalReference no TotalPass.
        var slot = new ClassSlot
        {
            TenantId = _tenantContext.TenantId,
            ClassId = wellhubClass.Id,
            StartsAt = request.StartsAt,
            EndsAt = request.EndsAt,
            Capacity = wellhubClass.PublishWellhub ? request.Capacity : 0,
            TotalPassCapacity = wellhubClass.PublishTotalPass ? request.TotalPassCapacity : 0,
        };

        if (wellhubClass.PublishWellhub && _gateway.IsConfigured && wellhubClass.ExternalId is not null && checkinPoint is not null)
        {
            slot.ExternalId = await _gateway.CreateSlotAsync(
                checkinPoint.ExternalId, wellhubClass.ExternalId, wellhubClass.ProductId,
                request.StartsAt, request.EndsAt, request.Capacity, ct);
        }

        if (wellhubClass.PublishTotalPass && _totalPass is { IsConfigured: true } && checkinPoint is not null)
        {
            await PublishToTotalPassAsync(checkinPoint, wellhubClass, slot, ct);
        }

        var created = await _slots.CreateAsync(slot, ct);
        return ToSlotDto(created);
    }

    /// <summary>Publica o horário como ocorrência avulsa no TotalPass. Falha não lança: o horário
    /// fica pendente (sem occurrenceUuid) e sobe de novo pelo "Sincronizar".</summary>
    private async Task<bool> PublishToTotalPassAsync(CheckinPoint point, WellhubClass wellhubClass, ClassSlot slot, CancellationToken ct)
    {
        if (_totalPass is null || !point.AcceptsTotalPass() || wellhubClass.TotalPassPlanId is not long planId) return false;

        var occurrence = await _totalPass.CreateOccurrenceAsync(point.TotalPassPlaceApiKey!, new TotalPassOccurrenceRequest(
            Title: wellhubClass.Name,
            Responsible: wellhubClass.Instructor ?? $"Equipe {point.Name}",
            Description: wellhubClass.Description,
            DurationMinutes: (int)Math.Round((slot.EndsAt - slot.StartsAt).TotalMinutes),
            Slots: slot.TotalPassCapacity,
            PlanId: planId,
            StartsAtLocal: SaoPauloTime.FromUtc(slot.StartsAt).DateTime,
            ExternalReference: slot.Id), ct);
        if (occurrence is null) return false;

        slot.TotalPassEventId = occurrence.EventId;
        slot.TotalPassOccurrenceUuid = occurrence.OccurrenceUuid;
        return true;
    }

    // ---- Gestão de Reservas ------------------------------------------------------------------

    /// <summary>Reservas do período já com aluno, aula, horário e unidade resolvidos — tela de
    /// Reservas. Resolve cada slot/categoria/unidade uma vez só, não por reserva.</summary>
    public async Task<IReadOnlyList<BookingListItemDto>> ListBookingsAsync(DateTime? start, DateTime? end, CancellationToken ct = default)
    {
        var tenantId = _tenantContext.TenantId;
        var bookings = await _bookings.ListAsync(tenantId, start, end, ct);
        if (bookings.Count == 0) return Array.Empty<BookingListItemDto>();

        var classes = (await _classes.ListAsync(tenantId, ct)).ToDictionary(c => c.Id);
        var points = (await _checkinPoints.ListAsync(tenantId, ct)).ToDictionary(p => p.Id);
        var students = (await _students.ListAsync(tenantId, ct)).ToDictionary(s => s.Id);

        var slots = new Dictionary<string, ClassSlot?>();
        foreach (var slotId in bookings.Select(b => b.SlotId).Distinct())
        {
            slots[slotId] = await _slots.GetByIdAsync(tenantId, slotId, ct);
        }

        return bookings.Select(b =>
        {
            var slot = slots.GetValueOrDefault(b.SlotId);
            var wellhubClass = slot is null ? null : classes.GetValueOrDefault(slot.ClassId);
            var point = wellhubClass is null ? null : points.GetValueOrDefault(wellhubClass.CheckinPointId);
            var student = b.StudentId is null ? null : students.GetValueOrDefault(b.StudentId);

            return new BookingListItemDto(
                b.Id, b.Status, b.RequestedAt, b.RespondedAt,
                b.StudentId, student?.Name, b.GympassId,
                wellhubClass?.Id, wellhubClass?.Name,
                point?.Id, point?.Name,
                slot?.StartsAt, b.App);
        }).ToList();
    }

    // ---- Gestão de Reservas (webhook) --------------------------------------------------------

    /// <summary>Evento <c>booking-requested</c>: confirma na hora se há vaga, senão rejeita. Aluno
    /// ainda não cadastrado é pré-registrado com os dados do próprio webhook (<paramref name="userInfo"/>),
    /// mesma regra do check-in (ver CheckinService.RegisterWellhubCheckinAsync).</summary>
    public async Task<BookingDto> HandleBookingRequestedAsync(
        string slotExternalId, string gympassId, string externalBookingId, DateTime requestedAt, string? rawPayload,
        WellhubUserInfo? userInfo = null, CancellationToken ct = default)
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
        if (student is null && !string.IsNullOrWhiteSpace(userInfo?.FirstName))
        {
            var name = string.Join(" ", new[] { userInfo.FirstName, userInfo.LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
            student = await _students.CreateAsync(new Student
            {
                TenantId = slot.TenantId,
                Name = name,
                Email = userInfo.Email,
                Phone = userInfo.PhoneNumber,
                WellhubMemberId = gympassId
            }, ct);
        }

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

    // ---- Reservas do TotalPass (webhook) --------------------------------------------------

    /// <summary>
    /// Webhook de reserva do TotalPass (chega pela URL da unidade, ver
    /// CheckinPoint.TotalPassWebhookKey). O TotalPass já confirmou a reserva do lado dele
    /// (hasSlotConfirmation=false, padrão), então aqui é só registro: reserva nova → Confirmed e
    /// +1 vaga ocupada; status de cancelamento → Canceled e −1. Idempotente pelo id da reserva lá.
    /// Horário desconhecido (aula criada fora do OmniClub) → ignorado, com log de quem chamou.
    /// </summary>
    public async Task<BookingDto?> HandleTotalPassBookingAsync(string webhookKey, TotalPassBookingData data, string? rawPayload, CancellationToken ct = default)
    {
        var point = await _checkinPoints.GetByTotalPassWebhookKeyAsync(webhookKey, ct)
            ?? throw new Exceptions.NotFoundException("Nenhuma unidade TotalPass cadastrada para esta URL de webhook.");

        var slot = await _slots.GetByTotalPassEventIdAsync(point.TenantId, data.EventId, ct);
        if (slot is null) return null;

        var externalBookingId = $"totalpass:{data.SlotId}";
        var existing = await _bookings.FindByExternalBookingIdAsync(point.TenantId, externalBookingId, ct);
        var canceled = IsCanceledStatus(data.Status);

        if (existing is not null)
        {
            if (canceled && existing.Status == BookingStatus.Confirmed)
            {
                existing.Status = BookingStatus.Canceled;
                existing.RespondedAt = DateTime.UtcNow;
                await _bookings.UpdateAsync(existing, ct);
                slot.TotalPassBookedCount = Math.Max(0, slot.TotalPassBookedCount - 1);
                await _slots.UpdateAsync(slot, ct);
            }
            var existingStudent = existing.StudentId is null ? null : await _students.GetByIdAsync(point.TenantId, existing.StudentId, ct);
            return ToBookingDto(existing, existingStudent);
        }

        var student = await TotalPassStudentResolver.FindOrCreateAsync(_students, point.TenantId, data.User, ct);
        var booking = new Booking
        {
            TenantId = point.TenantId,
            App = IntegrationApp.TotalPass,
            SlotId = slot.Id,
            StudentId = student?.Id,
            GympassId = data.User.Code ?? string.Empty,
            ExternalBookingId = externalBookingId,
            Status = canceled ? BookingStatus.Canceled : BookingStatus.Confirmed,
            RequestedAt = DateTime.UtcNow,
            RespondedAt = DateTime.UtcNow,
            RawPayload = rawPayload
        };
        await _bookings.CreateAsync(booking, ct);

        if (!canceled)
        {
            slot.TotalPassBookedCount++;
            await _slots.UpdateAsync(slot, ct);
        }
        return ToBookingDto(booking, student);
    }

    /// <summary>A doc só mostra "active" e "expired" como status de reserva; tratamos qualquer
    /// variação de cancelado/negado/removido como cancelamento.</summary>
    private static bool IsCanceledStatus(string? status) =>
        status is not null && (status.Contains("cancel", StringComparison.OrdinalIgnoreCase)
            || status.Contains("denied", StringComparison.OrdinalIgnoreCase)
            || status.Contains("removed", StringComparison.OrdinalIgnoreCase)
            || status.Contains("inactive", StringComparison.OrdinalIgnoreCase));

    private async Task<CheckinPoint> GetPointAsync(string checkinPointId, CancellationToken ct) =>
        await _checkinPoints.GetByIdAsync(_tenantContext.TenantId, checkinPointId, ct)
        ?? throw new Exceptions.NotFoundException($"Ponto de check-in '{checkinPointId}' não encontrado.");

    private void EnsureTotalPassConfigured()
    {
        if (_totalPass is null || !_totalPass.IsConfigured)
            throw new Exceptions.ConflictException("Integração TotalPass não configurada no servidor (falta a chave de integrador do OmniClub).");
    }

    private async Task SyncVacancyAsync(CheckinPoint? checkinPoint, WellhubClass? wellhubClass, ClassSlot slot, CancellationToken ct)
    {
        if (checkinPoint is null || wellhubClass?.ExternalId is null || slot.ExternalId is null) return;
        await _gateway.UpdateSlotVacancyAsync(checkinPoint.ExternalId, wellhubClass.ExternalId, slot.ExternalId, slot.Capacity, slot.BookedCount, ct);
    }

    private static ClassDto ToClassDto(WellhubClass c) => new(
        c.Id, c.CheckinPointId, c.Name, c.Description, c.ProductId, c.ExternalId, c.Active,
        c.PublishWellhub, c.PublishTotalPass, c.TotalPassPlanId, c.Instructor);

    private static ClassSlotDto ToSlotDto(ClassSlot s) => new(
        s.Id, s.ClassId, s.StartsAt, s.EndsAt, s.Capacity, s.BookedCount, s.ExternalId,
        s.TotalPassCapacity, s.TotalPassBookedCount, s.TotalPassOccurrenceUuid);

    private static BookingDto ToBookingDto(Booking b, Student? student) =>
        new(b.Id, b.SlotId, b.StudentId, student?.Name, b.GympassId, b.Status, b.RequestedAt, b.RespondedAt);
}
