using Microsoft.AspNetCore.Mvc;
using Qwader.Api.Authorization;
using Qwader.Application.Common;
using Qwader.Application.Inventory;
using Qwader.Domain.Identity;

namespace Qwader.Api.Controllers;

[ApiController]
[Route("api/inventory")]
[HasPermission(Permissions.InventoryView)]
public sealed class InventoryController(IInventoryService inventory) : ControllerBase
{
    [HttpGet("summary")]
    public Task<InventorySummaryDto> Summary(CancellationToken ct) => inventory.SummaryAsync(ct);

    [HttpGet("movements")]
    public Task<PagedResult<StockMovementDto>> Movements([FromQuery] MovementListRequest request, CancellationToken ct) =>
        inventory.MovementsAsync(request, ct);

    [HttpPost("adjustments")]
    [HasPermission(Permissions.InventoryAdjust)]
    public Task<StockAdjustmentResult> Adjust(StockAdjustmentRequest request, CancellationToken ct) => inventory.AdjustAsync(request, ct);

    [HttpGet("counts")]
    [HasPermission(Permissions.InventoryCount)]
    public Task<PagedResult<StockCountListItemDto>> Counts([FromQuery] PagedRequest request, CancellationToken ct) => inventory.CountsAsync(request, ct);

    [HttpGet("counts/{id:int}")]
    [HasPermission(Permissions.InventoryCount)]
    public Task<StockCountDto> Count(int id, CancellationToken ct) => inventory.GetCountAsync(id, ct);

    [HttpPost("counts")]
    [HasPermission(Permissions.InventoryCount)]
    public Task<StockCountDto> CreateCount(CreateStockCountRequest request, CancellationToken ct) => inventory.CreateCountAsync(request, ct);

    [HttpPut("counts/{id:int}")]
    [HasPermission(Permissions.InventoryCount)]
    public Task<StockCountDto> SaveCounts(int id, SaveCountsRequest request, CancellationToken ct) => inventory.SaveCountsAsync(id, request, ct);

    [HttpPost("counts/{id:int}/post")]
    [HasPermission(Permissions.InventoryCount)]
    public Task<StockCountDto> PostCount(int id, CancellationToken ct) => inventory.PostCountAsync(id, ct);

    [HttpPost("counts/{id:int}/cancel")]
    [HasPermission(Permissions.InventoryCount)]
    public async Task<IActionResult> CancelCount(int id, CancellationToken ct)
    {
        await inventory.CancelCountAsync(id, ct);
        return NoContent();
    }
}
