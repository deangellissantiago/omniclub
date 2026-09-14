namespace Checkin.Domain.Entities;

/// <summary>
/// Aluno da escola. Não possui login. O identificador de cada app (ex.: WellhubMemberId)
/// é o que permite casar um check-in recebido do app com o aluno cadastrado pelo admin.
/// </summary>
public class Student
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Document { get; set; } // CPF

    /// <summary>Wellhub ID (identificador único do usuário) — usado para casar check-ins recebidos.</summary>
    public string? WellhubMemberId { get; set; }

    /// <summary>Reservado para quando a integração TotalPass for implementada.</summary>
    public string? TotalPassMemberId { get; set; }

    public bool Active { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
