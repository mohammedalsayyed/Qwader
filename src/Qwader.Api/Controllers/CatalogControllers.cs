using Microsoft.AspNetCore.Mvc;
using Qwader.Api.Authorization;
using Qwader.Application.Catalog;
using Qwader.Application.Common;
using Qwader.Domain.Identity;

namespace Qwader.Api.Controllers;

[ApiController]
[Route("api/categories")]
[HasPermission(Permissions.ProductsView)]
public sealed class CategoriesController(ICategoryService categories) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<CategoryDto>> List([FromQuery] CategoryListRequest request, CancellationToken ct) => categories.ListAsync(request, ct);

    [HttpGet("{id:int}")]
    public Task<CategoryDto> Get(int id, CancellationToken ct) => categories.GetAsync(id, ct);

    [HttpPost]
    [HasPermission(Permissions.ProductsManage)]
    public Task<CategoryDto> Create(SaveCategoryRequest request, CancellationToken ct) => categories.CreateAsync(request, ct);

    [HttpPut("{id:int}")]
    [HasPermission(Permissions.ProductsManage)]
    public Task<CategoryDto> Update(int id, SaveCategoryRequest request, CancellationToken ct) => categories.UpdateAsync(id, request, ct);
}

[ApiController]
[Route("api/products")]
[HasPermission(Permissions.ProductsView)]
public sealed class ProductsController(IProductService products, IProductExcelService excel) : ControllerBase
{
    private const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    [HttpGet]
    public Task<PagedResult<ProductListItemDto>> List([FromQuery] ProductListRequest request, CancellationToken ct) => products.ListAsync(request, ct);

    [HttpGet("{id:int}")]
    public Task<ProductDetailDto> Get(int id, CancellationToken ct) => products.GetAsync(id, ct);

    /// <summary>Exact SKU/barcode lookup (barcode scanners).</summary>
    [HttpGet("by-code/{code}")]
    public async Task<ActionResult<ProductDetailDto>> ByCode(string code, CancellationToken ct) =>
        await products.FindByCodeAsync(code, ct) is { } product ? product : NotFound(new { title = "لا يوجد منتج بهذا الكود" });

    [HttpGet("code-available")]
    public async Task<object> CodeAvailable([FromQuery] string code, [FromQuery] int? exceptId, CancellationToken ct) =>
        new { available = !await products.IsCodeTakenAsync(code, exceptId, ct) };

    [HttpPost]
    [HasPermission(Permissions.ProductsManage)]
    public Task<ProductDetailDto> Create(SaveProductRequest request, CancellationToken ct) => products.CreateAsync(request, ct);

    [HttpPut("{id:int}")]
    [HasPermission(Permissions.ProductsManage)]
    public Task<ProductDetailDto> Update(int id, SaveProductRequest request, CancellationToken ct) => products.UpdateAsync(id, request, ct);

    [HttpPost("{id:int}/archive")]
    [HasPermission(Permissions.ProductsManage)]
    public async Task<IActionResult> Archive(int id, CancellationToken ct)
    {
        await products.SetArchivedAsync(id, true, ct);
        return NoContent();
    }

    [HttpPost("{id:int}/unarchive")]
    [HasPermission(Permissions.ProductsManage)]
    public async Task<IActionResult> Unarchive(int id, CancellationToken ct)
    {
        await products.SetArchivedAsync(id, false, ct);
        return NoContent();
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] ProductListRequest filter, CancellationToken ct) =>
        File(await excel.ExportAsync(filter, ct), XlsxType, $"products-{DateTime.UtcNow:yyyyMMdd}.xlsx");

    [HttpGet("import-template")]
    [HasPermission(Permissions.ProductsManage)]
    public IActionResult Template() => File(excel.Template(), XlsxType, "products-template.xlsx");

    [HttpPost("import")]
    [HasPermission(Permissions.ProductsManage)]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ProductImportResult> Import(IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        return await excel.ImportAsync(stream, ct);
    }
}
