namespace Checkin.Application.DTOs.Students;

public record StudentDto(
    string Id,
    string Name,
    string? Email,
    string? Phone,
    string? Document,
    string? WellhubMemberId,
    string? TotalPassMemberId,
    bool Active,
    DateTime CreatedAt);
