using Checkin.Application.Ports.Repositories;
using Checkin.Domain.Entities;
using Checkin.Domain.Enums;
using MongoDB.Driver;

namespace Checkin.Infrastructure.Persistence.Mongo.Repositories;

public class CheckinRecordRepository : ICheckinRecordRepository
{
    private readonly MongoContext _context;

    public CheckinRecordRepository(MongoContext context) => _context = context;

    public async Task<CheckinRecord> CreateAsync(CheckinRecord record, CancellationToken ct = default)
    {
        await _context.CheckinRecords.InsertOneAsync(record, cancellationToken: ct);
        return record;
    }

    public async Task<CheckinRecord?> FindByExternalCheckinIdAsync(string tenantId, string externalCheckinId, CancellationToken ct = default) =>
        await _context.CheckinRecords
            .Find(r => r.TenantId == tenantId && r.ExternalCheckinId == externalCheckinId)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<CheckinRecord>> ListRecentAsync(string tenantId, int take, CancellationToken ct = default) =>
        await _context.CheckinRecords.Find(r => r.TenantId == tenantId)
            .SortByDescending(r => r.OccurredAt).Limit(take).ToListAsync(ct);

    public async Task<IReadOnlyList<CheckinRecord>> ListAsync(
        string tenantId, DateTime? start, DateTime? end,
        string? studentId = null, string? checkinPointId = null, CancellationToken ct = default)
    {
        var filter = BuildFilter(tenantId, start, end, studentId, checkinPointId);
        return await _context.CheckinRecords.Find(filter).SortByDescending(r => r.OccurredAt).ToListAsync(ct);
    }

    public async Task<long> CountAsync(string tenantId, DateTime? start, DateTime? end, CancellationToken ct = default)
    {
        var filter = BuildFilter(tenantId, start, end, null, null);
        return await _context.CheckinRecords.CountDocumentsAsync(filter, cancellationToken: ct);
    }

    public async Task<long> CountByAppAsync(string tenantId, IntegrationApp app, DateTime? start, DateTime? end, CancellationToken ct = default)
    {
        var builder = Builders<CheckinRecord>.Filter;
        var filter = BuildFilter(tenantId, start, end, null, null) & builder.Eq(r => r.App, app);
        return await _context.CheckinRecords.CountDocumentsAsync(filter, cancellationToken: ct);
    }

    private static FilterDefinition<CheckinRecord> BuildFilter(
        string tenantId, DateTime? start, DateTime? end, string? studentId, string? checkinPointId)
    {
        var builder = Builders<CheckinRecord>.Filter;
        var filter = builder.Eq(r => r.TenantId, tenantId);

        if (start.HasValue) filter &= builder.Gte(r => r.OccurredAt, start.Value);
        if (end.HasValue) filter &= builder.Lte(r => r.OccurredAt, end.Value);
        if (!string.IsNullOrWhiteSpace(studentId)) filter &= builder.Eq(r => r.StudentId, studentId);
        if (!string.IsNullOrWhiteSpace(checkinPointId)) filter &= builder.Eq(r => r.CheckinPointId, checkinPointId);

        return filter;
    }
}
