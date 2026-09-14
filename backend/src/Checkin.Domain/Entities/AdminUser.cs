namespace Checkin.Domain.Entities;

/// <summary>
/// Administrador do sistema. É quem faz login, cadastra alunos e pontos de check-in
/// do seu tenant. Alunos, por ora, não possuem login (conforme requisito).
/// </summary>
public class AdminUser
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool Active { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
