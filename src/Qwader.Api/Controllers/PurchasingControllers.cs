using Microsoft.AspNetCore.Mvc;
using Qwader.Api.Authorization;
using Qwader.Application.Common;
using Qwader.Application.Purchasing;
using Qwader.Domain.Identity;

namespace Qwader.Api.Controllers;

[ApiController]
[Route("api/suppliers")]
[HasPermission(Permissions.SuppliersManage, Permissions.PurchasesView)]
public sealed class SuppliersController(ISupplierService suppliers) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<SupplierListItemDto>> List([FromQuery] SupplierListRequest request, CancellationToken ct) => suppliers.ListAsync(request, ct);

    [HttpGet("{id:int}")]
    public Task<SupplierDto> Get(int id, CancellationToken ct) => suppliers.GetAsync(id, ct);

    [HttpGet("{id:int}/statement")]
    public Task<StatementDto> Statement(int id, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        suppliers.StatementAsync(id, from, to, ct);

    [HttpPost]
    [HasPermission(Permissions.SuppliersManage)]
    public Task<SupplierDto> Create(SaveSupplierRequest request, CancellationToken ct) => suppliers.CreateAsync(request, ct);

    [HttpPut("{id:int}")]
    [HasPermission(Permissions.SuppliersManage)]
    public Task<SupplierDto> Update(int id, SaveSupplierRequest request, CancellationToken ct) => suppliers.UpdateAsync(id, request, ct);
}

[ApiController]
[Route("api/purchases")]
[HasPermission(Permissions.PurchasesView)]
public sealed class PurchasesController(IPurchaseService purchases) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<PurchaseListItemDto>> List([FromQuery] PurchaseListRequest request, CancellationToken ct) => purchases.ListAsync(request, ct);

    [HttpGet("{id:int}")]
    public Task<PurchaseDetailDto> Get(int id, CancellationToken ct) => purchases.GetAsync(id, ct);

    [HttpPost]
    [HasPermission(Permissions.PurchasesManage)]
    public Task<PurchaseDetailDto> Create(SavePurchaseRequest request, CancellationToken ct) => purchases.CreateAsync(request, ct);

    [HttpPut("{id:int}")]
    [HasPermission(Permissions.PurchasesManage)]
    public Task<PurchaseDetailDto> UpdateDraft(int id, SavePurchaseRequest request, CancellationToken ct) => purchases.UpdateDraftAsync(id, request, ct);

    [HttpPost("{id:int}/post")]
    [HasPermission(Permissions.PurchasesManage)]
    public Task<PurchaseDetailDto> Post(int id, PostPurchaseRequest request, CancellationToken ct) => purchases.PostAsync(id, request, ct);

    [HttpPost("{id:int}/void")]
    [HasPermission(Permissions.PurchasesManage)]
    public Task<PurchaseDetailDto> Void(int id, VoidRequest request, CancellationToken ct) => purchases.VoidAsync(id, request, ct);

    [HttpPost("{id:int}/returns")]
    [HasPermission(Permissions.PurchasesManage)]
    public Task<PurchaseDetailDto> Return(int id, PurchaseReturnRequest request, CancellationToken ct) => purchases.ReturnAsync(id, request, ct);
}
