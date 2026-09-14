using Checkin.Application.Ports.Repositories;
using Checkin.Domain.Entities;
using Checkin.Domain.Enums;
using MongoDB.Driver;

namespace Checkin.Infrastructure.Persistence.Mongo.Repositories;

public class CheckinPointRepository : ICheckinPointRepository
{
    private readonly MongoContext _context;

    public CheckinPointRepository(MongoContext context) => _context = context;

    public async Task<CheckinPoint?> GetByIdAsync(string tenantId, string id, CancellationToken ct = default) =>
        await _context.CheckinPoints.Find(p => p.TenantId == tenantId && p.Id == id).FirstOrDefaultAsync(ct);

    public async Task<CheckinPoint?> GetByExternalIdAsync(IntegrationApp app, string externalId, CancellationToken ct = default) =>
        await _context.CheckinPoints.Find(p => p.App == app && p.ExternalId == externalId).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<CheckinPoint>> ListAsync(string tenantId, CancellationToken ct = default) =>
        await _context.CheckinPoints.Find(p => p.TenantId == tenantId).SortByDescending(p => p.CreatedAt).ToListAsync(ct);

    public async Task<CheckinPoint> CreateAsync(CheckinPoint point, CancellationToken ct = default)
    {
        await _context.CheckinPoints.InsertOneAsync(point, cancellationToken: ct);
        return point;
    }

    public async Task<bool> UpdateAsync(CheckinPoint point, CancellationToken ct = default)
    {
        var result = await _context.CheckinPoints.ReplaceOneAsync(
            p => p.TenantId == point.TenantId && p.Id == point.Id, point, cancellationToken: ct);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> DeleteAsync(string tenantId, string id, CancellationToken ct = default)
    {
        var result = await _context.CheckinPoints.DeleteOneAsync(p => p.TenantId == tenantId && p.Id == id, ct);
        return result.DeletedCount > 0;
    }
}
