using Checkin.Application.DTOs.Auth;
using Checkin.Application.UseCases.Auth;
using Microsoft.AspNetCore.Mvc;

namespace Checkin.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;

    public AuthController(AuthService authService) => _authService = authService;

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await _authService.LoginAsync(request, ct);
        return Ok(result);
    }

    /// <summary>Cadastro autosserviço de um cliente novo (escola). Já loga automaticamente —
    /// o tenant nasce com assinatura Inactive, então o front deve mandar o admin direto pra
    /// tela de assinatura (ver README, "Cobrança / Stripe").</summary>
    [HttpPost("register")]
    public async Task<ActionResult<LoginResponse>> Register(RegisterRequest request, CancellationToken ct)
    {
        var result = await _authService.RegisterAsync(request, ct);
        return Ok(result);
    }
}
