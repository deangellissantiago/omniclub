using Checkin.Application.Ports.Repositories;
using Checkin.Domain.Entities;

namespace Checkin.Application.UseCases.Students;

/// <summary>Dados do usuário que o TotalPass manda nos webhooks (check-in e reserva).</summary>
public record TotalPassUserInfo(string? Code, string? Name, string? Email, string? Phone, string? DocumentNumber, string? DocumentType);

/// <summary>
/// Acha o aluno de um evento do TotalPass (check-in ou reserva): pelo código TotalPass; senão pelo
/// CPF (aluno que já existia, ex.: veio do Wellhub — vincula o código TotalPass a ele, sem
/// duplicar); senão cria com os dados do próprio evento. Sem nome no evento, devolve nulo.
/// </summary>
public static class TotalPassStudentResolver
{
    public static async Task<Student?> FindOrCreateAsync(IStudentRepository students, string tenantId, TotalPassUserInfo user, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(user.Code))
        {
            var byCode = await students.GetByTotalPassMemberIdAsync(tenantId, user.Code, ct);
            if (byCode is not null) return byCode;
        }

        var cpf = string.Equals(user.DocumentType, "cpf", StringComparison.OrdinalIgnoreCase) && user.DocumentNumber is not null
            ? new string(user.DocumentNumber.Where(char.IsDigit).ToArray())
            : null;
        if (!string.IsNullOrEmpty(cpf))
        {
            var byDocument = await students.GetByDocumentAsync(tenantId, cpf, ct);
            if (byDocument is not null)
            {
                if (!string.IsNullOrWhiteSpace(user.Code) && byDocument.TotalPassMemberId != user.Code)
                {
                    byDocument.TotalPassMemberId = user.Code;
                    await students.UpdateAsync(byDocument, ct);
                }
                return byDocument;
            }
        }

        if (string.IsNullOrWhiteSpace(user.Name)) return null;

        return await students.CreateAsync(new Student
        {
            TenantId = tenantId,
            Name = user.Name.Trim(),
            Email = user.Email,
            Phone = user.Phone,
            Document = cpf,
            TotalPassMemberId = user.Code
        }, ct);
    }
}
