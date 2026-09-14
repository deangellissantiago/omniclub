using Checkin.Domain.Entities;

namespace Checkin.Application.Ports.Security;

public interface IJwtTokenService
{
    string GenerateToken(AdminUser admin);
}
