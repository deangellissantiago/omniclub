using Checkin.Application.Ports.Repositories;
using Checkin.Domain.Entities;
using MongoDB.Driver;

namespace Checkin.Infrastructure.Persistence.Mongo.Repositories;

public class ClassSlotRepository : IClassSlotRepository
{
    private readonly MongoContext _context;

    public ClassSlotRepository(MongoContext context) => _context = context;

    public async Task<ClassSlot?> GetByIdAsync(string tenantId, string id, CancellationToken ct = default) =>
        await _context.ClassSlots.Find(s => s.TenantId == tenantId && s.Id == id).FirstOrDefaultAsync(ct);

    public async Task<ClassSlot?> GetByExternalIdAsync(string externalId, CancellationToken ct = default) =>
        await _context.ClassSlots.Find(s => s.ExternalId == externalId).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<ClassSlot>> ListByClassAsync(string tenantId, string classId, CancellationToken ct = default) =>
        await _context.ClassSlots.Find(s => s.TenantId == tenantId && s.ClassId == classId).SortBy(s => s.StartsAt).ToListAsync(ct);

    public async Task<ClassSlot> CreateAsync(ClassSlot slot, CancellationToken ct = default)
    {
        await _context.ClassSlots.InsertOneAsync(slot, cancellationToken: ct);
        return slot;
    }

    public async Task<bool> UpdateAsync(ClassSlot slot, CancellationToken ct = default)
    {
        var result = await _context.ClassSlots.ReplaceOneAsync(
            s => s.TenantId == slot.TenantId && s.Id == slot.Id, slot, cancellationToken: ct);
        return result.ModifiedCount > 0;
    }
}
