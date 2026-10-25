using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Qwader.Application.Abstractions;
using Qwader.Application.Common;
using Qwader.Domain.Administration;
using Qwader.Domain.Catalog;

namespace Qwader.Application.Catalog;

public sealed record CategoryDto(int Id, string Code, string Name, string? Description, bool IsActive, int ProductCount);

public sealed record SaveCategoryRequest(string? Code, string Name, string? Description, bool IsActive = true);

public sealed class CategoryListRequest : PagedRequest
{
    public bool? IsActive { get; set; }
}

public interface ICategoryService
{
    Task<PagedResult<CategoryDto>> ListAsync(CategoryListRequest request, CancellationToken ct = default);
    Task<CategoryDto> GetAsync(int id, CancellationToken ct = default);
    Task<CategoryDto> CreateAsync(SaveCategoryRequest request, CancellationToken ct = default);
    Task<CategoryDto> UpdateAsync(int id, SaveCategoryRequest request, CancellationToken ct = default);
}

public sealed class SaveCategoryRequestValidator : AbstractValidator<SaveCategoryRequest>
{
    public SaveCategoryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("اسم الصنف مطلوب").MaximumLength(100).WithMessage("اسم الصنف طويل جدًا");
        RuleFor(x => x.Code).MaximumLength(30).WithMessage("الكود طويل جدًا")
            .Matches(@"^\S*$").WithMessage("الكود يجب ألا يحتوي على مسافات");
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

public sealed class CategoryService(IAppDbContext db, IDocumentNumberGenerator numbers, IAuditService audit) : ICategoryService
{
    public async Task<PagedResult<CategoryDto>> ListAsync(CategoryListRequest request, CancellationToken ct = default)
    {
        var query = db.Categories.AsNoTracking();
        if (request.IsActive is { } active) query = query.Where(c => c.IsActive == active);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = ArabicText.Normalize(request.Search);
            var raw = request.Search.Trim();
            query = query.Where(c => c.NormalizedName.Contains(term) || c.Code.StartsWith(raw));
        }

        return await query.OrderByDescending(c => c.IsActive).ThenBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Code, c.Name, c.Description, c.IsActive, c.Products.Count(p => !p.IsArchived)))
            .ToPagedAsync(request, ct);
    }

    public async Task<CategoryDto> GetAsync(int id, CancellationToken ct = default) =>
        await db.Categories.AsNoTracking().Where(c => c.Id == id)
            .Select(c => new CategoryDto(c.Id, c.Code, c.Name, c.Description, c.IsActive, c.Products.Count(p => !p.IsArchived)))
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("الصنف غير موجود");

    public async Task<CategoryDto> CreateAsync(SaveCategoryRequest request, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var category = new Category();
        await ApplyAsync(category, request, ct);
        category.Code = string.IsNullOrWhiteSpace(request.Code) ? await numbers.NextAsync(DocumentType.Category, ct) : request.Code.Trim();
        await EnsureUniqueAsync(category, ct);

        db.Categories.Add(category);
        await db.SaveChangesAsync(ct);
        audit.Log("Category.Create", "Category", category.Id.ToString(), null, request);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(category.Id, ct);
    }

    public async Task<CategoryDto> UpdateAsync(int id, SaveCategoryRequest request, CancellationToken ct = default)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("الصنف غير موجود");
        var before = new { category.Code, category.Name, category.Description, category.IsActive };

        await ApplyAsync(category, request, ct);
        if (!string.IsNullOrWhiteSpace(request.Code)) category.Code = request.Code.Trim();
        await EnsureUniqueAsync(category, ct);

        audit.Log("Category.Update", "Category", id.ToString(), before, request);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    private static Task ApplyAsync(Category category, SaveCategoryRequest request, CancellationToken _)
    {
        category.Name = request.Name.Trim();
        category.NormalizedName = ArabicText.Normalize(request.Name);
        category.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        category.IsActive = request.IsActive;
        return Task.CompletedTask;
    }

    private async Task EnsureUniqueAsync(Category category, CancellationToken ct)
    {
        if (await db.Categories.AnyAsync(c => c.Id != category.Id && c.Code == category.Code, ct))
            throw new ValidationFailedException("code", "كود الصنف مستخدم من قبل");
        if (await db.Categories.AnyAsync(c => c.Id != category.Id && c.Name == category.Name, ct))
            throw new ValidationFailedException("name", "يوجد صنف بنفس الاسم");
    }
}
