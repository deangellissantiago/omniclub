using Checkin.Application.DTOs.Auth;
using Checkin.Application.Exceptions;
using Checkin.Application.Ports.Repositories;
using Checkin.Application.Ports.Security;
using Checkin.Domain.Entities;

namespace Checkin.Application.UseCases.Auth;

public class AuthService
{
    private readonly IAdminUserRepository _adminUserRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;

    public AuthService(
        IAdminUserRepository adminUserRepository,
        ITenantRepository tenantRepository,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService)
    {
        _adminUserRepository = adminUserRepository;
        _tenantRepository = tenantRepository;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var admin = await _adminUserRepository.GetByEmailAsync(request.Email, ct);
        if (admin is null || !admin.Active || !_passwordHasher.Verify(request.Password, admin.PasswordHash))
        {
            throw new UnauthorizedAccessException("E-mail ou senha inválidos.");
        }

        var tenant = await _tenantRepository.GetByIdAsync(admin.TenantId, ct)
            ?? throw new NotFoundException("Tenant não encontrado para este administrador.");

        var token = _jwtTokenService.GenerateToken(admin);
        return new LoginResponse(token, admin.Name, tenant.Id, tenant.Name);
    }

    /// <summary>Cadastro autosserviço: cria o Tenant (escola) + o primeiro AdminUser dele, e já
    /// devolve o token (login automático) — o tenant nasce com assinatura Inactive; o cliente só
    /// consegue usar o sistema de verdade depois de assinar (ver BillingService, README
    /// "Cobrança / Stripe").</summary>
    public async Task<LoginResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        if (await _adminUserRepository.GetByEmailAsync(request.Email, ct) is not null)
        {
            throw new ConflictException("Já existe uma conta cadastrada com este e-mail.");
        }

        // Explícito, não só o default da classe (que é Active — ver Tenant.SubscriptionStatus):
        // cadastro autosserviço sempre nasce Inactive, precisa passar pelo checkout do Stripe.
        var tenant = await _tenantRepository.CreateAsync(
            new Tenant { Name = request.TenantName, SubscriptionStatus = Domain.Enums.SubscriptionStatus.Inactive }, ct);

        var admin = await _adminUserRepository.CreateAsync(new AdminUser
        {
            TenantId = tenant.Id,
            Name = request.AdminName,
            Email = request.Email,
            PasswordHash = _passwordHasher.Hash(request.Password)
        }, ct);

        var token = _jwtTokenService.GenerateToken(admin);
        return new LoginResponse(token, admin.Name, tenant.Id, tenant.Name);
    }
}
