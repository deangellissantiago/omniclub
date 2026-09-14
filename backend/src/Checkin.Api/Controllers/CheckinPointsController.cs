using Checkin.Application.DTOs.CheckinPoints;
using Checkin.Application.UseCases.CheckinPoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Checkin.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/checkin-points")]
public class CheckinPointsController : ControllerBase
{
    private readonly CheckinPointService _service;

    public CheckinPointsController(CheckinPointService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CheckinPointDto>>> List(CancellationToken ct) => Ok(await _service.ListAsync(ct));

    [HttpPost]
    public async Task<ActionResult<CheckinPointDto>> Create(UpsertCheckinPointRequest request, CancellationToken ct) =>
        Ok(await _service.CreateAsync(request, ct));

    [HttpPut("{id}")]
    public async Task<ActionResult<CheckinPointDto>> Update(string id, UpsertCheckinPointRequest request, CancellationToken ct) =>
        Ok(await _service.UpdateAsync(id, request, ct));

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        await _service.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>Busca os produtos (planos/tipos de acesso) reais deste ponto no Wellhub e
    /// vincula ao cadastro, pra ficarem visíveis (ver CheckinPointService.SyncProductsAsync).</summary>
    [HttpPost("{id}/sync-products")]
    public async Task<ActionResult<CheckinPointDto>> SyncProducts(string id, CancellationToken ct) =>
        Ok(await _service.SyncProductsAsync(id, ct));
}
