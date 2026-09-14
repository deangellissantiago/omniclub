using Checkin.Application.DTOs.Students;
using Checkin.Application.UseCases.Students;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Checkin.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/students")]
public class StudentsController : ControllerBase
{
    private readonly StudentService _service;

    public StudentsController(StudentService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<StudentDto>>> List(CancellationToken ct) => Ok(await _service.ListAsync(ct));

    [HttpGet("{id}")]
    public async Task<ActionResult<StudentDto>> Get(string id, CancellationToken ct) => Ok(await _service.GetAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<StudentDto>> Create(UpsertStudentRequest request, CancellationToken ct)
    {
        var created = await _service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<StudentDto>> Update(string id, UpsertStudentRequest request, CancellationToken ct) =>
        Ok(await _service.UpdateAsync(id, request, ct));

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        await _service.DeleteAsync(id, ct);
        return NoContent();
    }
}
