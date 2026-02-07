using InventoryTracker.Domain.Auth;
using InventoryTracker.Infrastructure.Dtos;
using InventoryTracker.Infrastructure.Idempotency.Services;
using InventoryTracker.Infrastructure.Identity;
using InventoryTracker.Infrastructure.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace InventoryTracker.Api.Controllers;

[ApiController]
[Route("api/inventory-movements")]
public sealed class InventoryMovementsController : ControllerBase
{
    private readonly IInventoryMovementService _svc;
    private readonly IIdempotencyService _idem;
    private readonly ICurrentUserService _current;

    public InventoryMovementsController(
        IInventoryMovementService svc,
        IIdempotencyService idem,
        ICurrentUserService current)
    {
        _svc = svc;
        _idem = idem;
        _current = current;
    }

    [HttpPost]
    [Authorize(Roles = AppRoles.CanManageMasterData)]
    public async Task<IActionResult> CreateDraft([FromBody] InventoryMovementDtos.CreateDraftRequest req, CancellationToken ct)
        => await Idempotent("POST:/api/inventory-movements", async () =>
        {
            var res = await _svc.CreateDraftAsync(req, ct);
            return (200, res);
        }, ct);

    [HttpPut("{id:guid}")]
    [Authorize(Roles = AppRoles.CanManageMasterData)]
    public async Task<IActionResult> UpdateDraft(Guid id, [FromBody] InventoryMovementDtos.UpdateDraftRequest req, CancellationToken ct)
        => await Idempotent($"PUT:/api/inventory-movements/{id}", async () =>
        {
            var res = await _svc.UpdateDraftAsync(id, req, ct);
            return (200, res);
        }, ct);

    [HttpPost("{id:guid}/lines")]
    [Authorize(Roles = AppRoles.CanManageMasterData)]
    public async Task<IActionResult> AddLine(Guid id, [FromBody] InventoryMovementDtos.LineRequest req, CancellationToken ct)
        => await Idempotent($"POST:/api/inventory-movements/{id}/lines", async () =>
        {
            var res = await _svc.AddLineAsync(id, req, ct);
            return (200, res);
        }, ct);

    [HttpPut("{id:guid}/lines")]
    [Authorize(Roles = AppRoles.CanManageMasterData)]
    public async Task<IActionResult> ReplaceLines(Guid id, [FromBody] InventoryMovementDtos.ReplaceLinesRequest req, CancellationToken ct)
        => await Idempotent($"PUT:/api/inventory-movements/{id}/lines", async () =>
        {
            var res = await _svc.ReplaceLinesAsync(id, req, ct);
            return (200, res);
        }, ct);

    [HttpPost("{id:guid}/post")]
    [Authorize(Roles = AppRoles.CanManageMasterData)]
    public async Task<IActionResult> Post(Guid id, CancellationToken ct)
        => await Idempotent($"POST:/api/inventory-movements/{id}/post", async () =>
        {
            var res = await _svc.PostAsync(id, ct);
            return (200, res);
        }, ct);

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = AppRoles.CanManageMasterData)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
        => await Idempotent($"POST:/api/inventory-movements/{id}/cancel", async () =>
        {
            var res = await _svc.CancelAsync(id, ct);
            return (200, res);
        }, ct);

    // ---------- helpers ----------

    private async Task<IActionResult> Idempotent<T>(
        string endpointKey,
        Func<Task<(int StatusCode, T Body)>> work,
        CancellationToken ct)
    {
        var idemKey = Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(idemKey))
        {
            // No idempotency: normal processing
            return await Execute(work, ct);
        }

        var userId = _current.UserId;

        // Cache hit?
        var cached = await _idem.TryGetCachedAsync(idemKey, endpointKey, userId, ct);
        if (cached.Hit)
        {
            var cachedBody = JsonSerializer.Deserialize<T>(cached.ResponseJson);
            if (cachedBody is null) return StatusCode(cached.StatusCode);
            return StatusCode(cached.StatusCode, cachedBody);
        }

        // Begin record
        var recordId = await _idem.BeginAsync(idemKey, endpointKey, userId, ct);

        try
        {
            var (status, body) = await work();

            var json = JsonSerializer.Serialize(body);
            await _idem.CompleteAsync(recordId, succeeded: true, statusCode: status, responseJson: json, errorMessage: null, ct);

            return StatusCode(status, body);
        }
        catch (Exception ex)
        {
            await _idem.CompleteAsync(recordId, succeeded: false, statusCode: 500, responseJson: null, errorMessage: ex.Message, ct);
            return Problem(ex.Message);
        }
    }

    private static async Task<IActionResult> Execute<T>(Func<Task<(int StatusCode, T Body)>> work, CancellationToken ct)
    {
        _ = ct;
        var (status, body) = await work();
        return new ObjectResult(body) { StatusCode = status };
    }
}
