using Checkin.Domain.Enums;

namespace Checkin.Application.DTOs.Checkins;

public record CheckinDto(
    string Id,
    string? StudentId,
    string? StudentName,
    string CheckinPointId,
    string? CheckinPointName,
    IntegrationApp App,
    DateTime OccurredAt,
    CheckinStatus Status);

/// <summary>Dados do usuário que vêm no próprio webhook de check-in — usados para pré-registrar
/// o aluno automaticamente quando o Wellhub ID ainda não está cadastrado (ver e-mail do Wellhub
/// Technical Sales: "que pode optar por fazer um pré-registro do usuário").</summary>
public record WellhubUserInfo(string? FirstName, string? LastName, string? Email, string? PhoneNumber)
{
    public static readonly WellhubUserInfo Empty = new(null, null, null, null);
}
