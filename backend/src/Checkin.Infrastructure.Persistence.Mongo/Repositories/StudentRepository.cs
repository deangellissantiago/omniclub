using Checkin.Application.Ports.Repositories;
using Checkin.Domain.Entities;
using Checkin.Domain.Enums;
using MongoDB.Driver;

namespace Checkin.Infrastructure.Persistence.Mongo.Repositories;

public class StudentRepository : IStudentRepository
{
    private readonly MongoContext _context;

    public StudentRepository(MongoContext context) => _context = context;

    public async Task<Student?> GetByIdAsync(string tenantId, string id, CancellationToken ct = default) =>
        await _context.Students.Find(s => s.TenantId == tenantId && s.Id == id).FirstOrDefaultAsync(ct);

    public async Task<Student?> GetByWellhubMemberIdAsync(string wellhubMemberId, CancellationToken ct = default) =>
        await _context.Students.Find(s => s.WellhubMemberId == wellhubMemberId).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<Student>> ListAsync(string tenantId, CancellationToken ct = default) =>
        await _context.Students.Find(s => s.TenantId == tenantId).SortByDescending(s => s.CreatedAt).ToListAsync(ct);

    public async Task<Student> CreateAsync(Student student, CancellationToken ct = default)
    {
        await _context.Students.InsertOneAsync(student, cancellationToken: ct);
        return student;
    }

    public async Task<bool> UpdateAsync(Student student, CancellationToken ct = default)
    {
        var result = await _context.Students.ReplaceOneAsync(
            s => s.TenantId == student.TenantId && s.Id == student.Id, student, cancellationToken: ct);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> DeleteAsync(string tenantId, string id, CancellationToken ct = default)
    {
        var result = await _context.Students.DeleteOneAsync(s => s.TenantId == tenantId && s.Id == id, ct);
        return result.DeletedCount > 0;
    }

    public async Task<long> CountByAppAsync(string tenantId, IntegrationApp app, CancellationToken ct = default)
    {
        var builder = Builders<Student>.Filter;
        var tenantFilter = builder.Eq(s => s.TenantId, tenantId);

        var appFilter = app switch
        {
            IntegrationApp.Wellhub => builder.Ne(s => s.WellhubMemberId, null) & builder.Ne(s => s.WellhubMemberId, string.Empty),
            IntegrationApp.TotalPass => builder.Ne(s => s.TotalPassMemberId, null) & builder.Ne(s => s.TotalPassMemberId, string.Empty),
            _ => FilterDefinition<Student>.Empty
        };

        return await _context.Students.CountDocumentsAsync(tenantFilter & appFilter, cancellationToken: ct);
    }
}
