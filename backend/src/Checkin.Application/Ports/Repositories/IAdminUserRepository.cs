using Checkin.Domain.Entities;

namespace Checkin.Application.Ports.Repositories;

public interface IAdminUserRepository
{
    Task<AdminUser?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<AdminUser?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<AdminUser> CreateAsync(AdminUser admin, CancellationToken ct = default);
}
