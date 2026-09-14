namespace Checkin.Application.DTOs.Auth;

/// <summary>Cadastro autosserviço: cria o Tenant (escola) + o primeiro AdminUser dele. O tenant
/// nasce com assinatura Inactive — ver AuthService.RegisterAsync e README "Cobrança / Stripe".</summary>
public record RegisterRequest(string TenantName, string AdminName, string Email, string Password);
