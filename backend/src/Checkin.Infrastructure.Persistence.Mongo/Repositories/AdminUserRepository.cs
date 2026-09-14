using Checkin.Application.Ports.Repositories;
using Checkin.Domain.Entities;
using MongoDB.Driver;

namespace Checkin.Infrastructure.Persistence.Mongo.Repositories;

public class AdminUserRepository : IAdminUserRepository
{
    private readonly MongoContext _context;

    public AdminUserRepository(MongoContext context) => _context = context;

    public async Task<AdminUser?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        await _context.AdminUsers.Find(a => a.Email == email).FirstOrDefaultAsync(ct);

    public async Task<AdminUser?> GetByIdAsync(string id, CancellationToken ct = default) =>
        await _context.AdminUsers.Find(a => a.Id == id).FirstOrDefaultAsync(ct);

    public async Task<AdminUser> CreateAsync(AdminUser admin, CancellationToken ct = default)
    {
        await _context.AdminUsers.InsertOneAsync(admin, cancellationToken: ct);
        return admin;
    }
}
