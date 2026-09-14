using System.Security.Claims;
using Checkin.Application.Ports;

namespace Checkin.Api.Security;

public class CurrentTenantContext : ICurrentTenantContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentTenantContext(IHttpContextAccessor httpContextAccessor) => _httpContextAccessor = httpContextAccessor;

    public string TenantId => _httpContextAccessor.HttpContext?.User.FindFirst("tenantId")?.Value
        ?? throw new UnauthorizedAccessException("Tenant não identificado na requisição.");

    public string AdminId => _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? throw new UnauthorizedAccessException("Administrador não identificado na requisição.");
}
