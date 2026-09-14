using Checkin.Application.Ports.Repositories;
using Checkin.Domain.Entities;
using MongoDB.Driver;

namespace Checkin.Infrastructure.Persistence.Mongo.Repositories;

public class ClassRepository : IClassRepository
{
    private readonly MongoContext _context;

    public ClassRepository(MongoContext context) => _context = context;

    public async Task<WellhubClass?> GetByIdAsync(string tenantId, string id, CancellationToken ct = default) =>
        await _context.Classes.Find(c => c.TenantId == tenantId && c.Id == id).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<WellhubClass>> ListAsync(string tenantId, CancellationToken ct = default) =>
        await _context.Classes.Find(c => c.TenantId == tenantId).SortByDescending(c => c.CreatedAt).ToListAsync(ct);

    public async Task<WellhubClass> CreateAsync(WellhubClass wellhubClass, CancellationToken ct = default)
    {
        await _context.Classes.InsertOneAsync(wellhubClass, cancellationToken: ct);
        return wellhubClass;
    }

    public async Task<bool> UpdateAsync(WellhubClass wellhubClass, CancellationToken ct = default)
    {
        var result = await _context.Classes.ReplaceOneAsync(
            c => c.TenantId == wellhubClass.TenantId && c.Id == wellhubClass.Id, wellhubClass, cancellationToken: ct);
        return result.ModifiedCount > 0;
    }
}
