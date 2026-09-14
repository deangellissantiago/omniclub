namespace Checkin.Application.DTOs.Auth;

public record LoginResponse(string Token, string AdminName, string TenantId, string TenantName);
