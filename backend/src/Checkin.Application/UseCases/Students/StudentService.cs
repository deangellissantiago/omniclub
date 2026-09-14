using Checkin.Application.DTOs.Students;
using Checkin.Application.Exceptions;
using Checkin.Application.Ports;
using Checkin.Application.Ports.Repositories;
using Checkin.Domain.Entities;

namespace Checkin.Application.UseCases.Students;

public class StudentService
{
    private readonly IStudentRepository _repository;
    private readonly ICurrentTenantContext _tenantContext;

    public StudentService(IStudentRepository repository, ICurrentTenantContext tenantContext)
    {
        _repository = repository;
        _tenantContext = tenantContext;
    }

    public async Task<IReadOnlyList<StudentDto>> ListAsync(CancellationToken ct = default)
    {
        var students = await _repository.ListAsync(_tenantContext.TenantId, ct);
        return students.Select(ToDto).ToList();
    }

    public async Task<StudentDto> GetAsync(string id, CancellationToken ct = default)
    {
        var student = await _repository.GetByIdAsync(_tenantContext.TenantId, id, ct)
            ?? throw new NotFoundException("Aluno não encontrado.");
        return ToDto(student);
    }

    public async Task<StudentDto> CreateAsync(UpsertStudentRequest request, CancellationToken ct = default)
    {
        var student = new Student
        {
            TenantId = _tenantContext.TenantId,
            Name = request.Name,
            Email = request.Email,
            Phone = request.Phone,
            Document = request.Document,
            WellhubMemberId = request.WellhubMemberId,
            TotalPassMemberId = request.TotalPassMemberId,
            Active = request.Active
        };
        var created = await _repository.CreateAsync(student, ct);
        return ToDto(created);
    }

    public async Task<StudentDto> UpdateAsync(string id, UpsertStudentRequest request, CancellationToken ct = default)
    {
        var student = await _repository.GetByIdAsync(_tenantContext.TenantId, id, ct)
            ?? throw new NotFoundException("Aluno não encontrado.");

        student.Name = request.Name;
        student.Email = request.Email;
        student.Phone = request.Phone;
        student.Document = request.Document;
        student.WellhubMemberId = request.WellhubMemberId;
        student.TotalPassMemberId = request.TotalPassMemberId;
        student.Active = request.Active;
        student.UpdatedAt = DateTime.UtcNow;

        await _repository.UpdateAsync(student, ct);
        return ToDto(student);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        var deleted = await _repository.DeleteAsync(_tenantContext.TenantId, id, ct);
        if (!deleted) throw new NotFoundException("Aluno não encontrado.");
    }

    private static StudentDto ToDto(Student s) => new(
        s.Id, s.Name, s.Email, s.Phone, s.Document, s.WellhubMemberId, s.TotalPassMemberId, s.Active, s.CreatedAt);
}
