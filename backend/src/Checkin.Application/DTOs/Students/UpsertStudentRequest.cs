namespace Checkin.Application.DTOs.Students;

public record UpsertStudentRequest(
    string Name,
    string? Email,
    string? Phone,
    string? Document,
    string? WellhubMemberId,
    string? TotalPassMemberId,
    bool Active,
    /// <summary>Opcional — usada só pela régua de relacionamento automática (mensagem de
    /// aniversário via WhatsApp, ver RetentionAlertService).</summary>
    DateTime? BirthDate = null);
