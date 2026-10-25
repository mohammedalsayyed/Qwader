using Microsoft.AspNetCore.Mvc;
using Qwader.Api.Authorization;
using Qwader.Application.Common;
using Qwader.Application.Customers;
using Qwader.Application.Purchasing;
using Qwader.Application.Sales;
using Qwader.Domain.Identity;

namespace Qwader.Api.Controllers;

[ApiController]
[Route("api/sales")]
[HasPermission(Permissions.SalesCreate, Permissions.SalesViewAll)]
public sealed class SalesController(ISaleService sales) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<SaleListItemDto>> List([FromQuery] SaleListRequest request, CancellationToken ct) => sales.ListAsync(request, ct);

    /// <summary>Today's (or a period's) totals for the dashboard. Staff without report access see their own sales.</summary>
    [HttpGet("summary")]
    [HasPermission(Permissions.SalesCreate, Permissions.SalesViewAll, Permissions.ReportsSales)]
    public Task<SalesSummaryDto> Summary([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) => sales.SummaryAsync(from, to, ct);

    [HttpGet("{id:int}")]
    public Task<SaleDetailDto> Get(int id, CancellationToken ct) => sales.GetAsync(id, ct);

    [HttpPost]
    [HasPermission(Permissions.SalesCreate)]
    public Task<SaleDetailDto> Create(CreateSaleRequest request, CancellationToken ct) => sales.CreateAsync(request, ct);

    [HttpPost("{id:int}/returns")]
    [HasPermission(Permissions.SalesReturn)]
    public Task<SaleDetailDto> Return(int id, SaleReturnRequest request, CancellationToken ct) => sales.ReturnAsync(id, request, ct);

    [HttpPost("{id:int}/void")]
    [HasPermission(Permissions.SalesVoid)]
    public Task<SaleDetailDto> Void(int id, VoidRequest request, CancellationToken ct) => sales.VoidAsync(id, request, ct);
}

[ApiController]
[Route("api/customers")]
[HasPermission(Permissions.CustomersManage, Permissions.SalesCreate)]
public sealed class CustomersController(ICustomerService customers) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<CustomerListItemDto>> List([FromQuery] CustomerListRequest request, CancellationToken ct) => customers.ListAsync(request, ct);

    [HttpGet("{id:int}")]
    public Task<CustomerDto> Get(int id, CancellationToken ct) => customers.GetAsync(id, ct);

    [HttpGet("{id:int}/statement")]
    public Task<StatementDto> Statement(int id, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        customers.StatementAsync(id, from, to, ct);

    [HttpPost]
    public Task<CustomerDto> Create(SaveCustomerRequest request, CancellationToken ct) => customers.CreateAsync(request, ct);

    [HttpPut("{id:int}")]
    [HasPermission(Permissions.CustomersManage)]
    public Task<CustomerDto> Update(int id, SaveCustomerRequest request, CancellationToken ct) => customers.UpdateAsync(id, request, ct);
}
