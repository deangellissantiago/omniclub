using Checkin.Application.Ports.Repositories;
using Checkin.Domain.Entities;

namespace Checkin.Tests.Fakes;

public class FakeClassRepository : IClassRepository
{
    public List<WellhubClass> Classes { get; } = new();

    public Task<WellhubClass?> GetByIdAsync(string tenantId, string id, CancellationToken ct = default) =>
        Task.FromResult(Classes.FirstOrDefault(c => c.TenantId == tenantId && c.Id == id));

    public Task<IReadOnlyList<WellhubClass>> ListAsync(string tenantId, CancellationToken ct = default) =>
        Task.FromResult((IReadOnlyList<WellhubClass>)Classes.Where(c => c.TenantId == tenantId).ToList());

    public Task<WellhubClass> CreateAsync(WellhubClass wellhubClass, CancellationToken ct = default)
    {
        Classes.Add(wellhubClass);
        return Task.FromResult(wellhubClass);
    }

    public Task<bool> UpdateAsync(WellhubClass wellhubClass, CancellationToken ct = default) => Task.FromResult(true);
}

public class FakeClassSlotRepository : IClassSlotRepository
{
    public List<ClassSlot> Slots { get; } = new();
    public int UpdateCallCount { get; private set; }

    public Task<ClassSlot?> GetByIdAsync(string tenantId, string id, CancellationToken ct = default) =>
        Task.FromResult(Slots.FirstOrDefault(s => s.TenantId == tenantId && s.Id == id));

    public Task<ClassSlot?> GetByExternalIdAsync(string externalId, CancellationToken ct = default) =>
        Task.FromResult(Slots.FirstOrDefault(s => s.ExternalId == externalId));

    public Task<IReadOnlyList<ClassSlot>> ListByClassAsync(string tenantId, string classId, CancellationToken ct = default) =>
        Task.FromResult((IReadOnlyList<ClassSlot>)Slots.Where(s => s.TenantId == tenantId && s.ClassId == classId).ToList());

    public Task<ClassSlot> CreateAsync(ClassSlot slot, CancellationToken ct = default)
    {
        Slots.Add(slot);
        return Task.FromResult(slot);
    }

    public Task<bool> UpdateAsync(ClassSlot slot, CancellationToken ct = default)
    {
        UpdateCallCount++;
        return Task.FromResult(true);
    }
}

public class FakeBookingRepository : IBookingRepository
{
    public List<Booking> Bookings { get; } = new();
    public int CreateCallCount { get; private set; }

    public Task<Booking> CreateAsync(Booking booking, CancellationToken ct = default)
    {
        CreateCallCount++;
        Bookings.Add(booking);
        return Task.FromResult(booking);
    }

    public Task<bool> UpdateAsync(Booking booking, CancellationToken ct = default) => Task.FromResult(true);

    public Task<Booking?> FindByExternalBookingIdAsync(string tenantId, string externalBookingId, CancellationToken ct = default) =>
        Task.FromResult(Bookings.FirstOrDefault(b => b.TenantId == tenantId && b.ExternalBookingId == externalBookingId));

    public Task<IReadOnlyList<Booking>> ListAsync(string tenantId, DateTime? start, DateTime? end, CancellationToken ct = default)
    {
        IEnumerable<Booking> query = Bookings.Where(b => b.TenantId == tenantId);
        if (start.HasValue) query = query.Where(b => b.RequestedAt >= start.Value);
        if (end.HasValue) query = query.Where(b => b.RequestedAt <= end.Value);
        return Task.FromResult((IReadOnlyList<Booking>)query.ToList());
    }
}
