using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Qwader.Application.Abstractions;
using Qwader.Application.Common;
using Qwader.Domain.Administration;
using Qwader.Domain.Catalog;

namespace Qwader.Application.Catalog;

public sealed record CategoryDto(int Id, string Code, string Name, string? Description, bool IsActive, int ProductCount,
    int? GroupId = null, string? GroupName = null);

/// <param name="GroupId">Its general category («الصنف العام»); null: none.</param>
public sealed record SaveCategoryRequest(string? Code, string Name, string? Description, bool IsActive = true, int? GroupId = null);

public sealed class CategoryListRequest : PagedRequest
{
    public bool? IsActive { get; set; }
    public int? GroupId { get; set; }
}

/// <summary>A general category with how many categories and (not archived) products are under it.</summary>
public sealed record CategoryGroupDto(int Id, string Name, int CategoryCount, int ProductCount);

public sealed record SaveCategoryGroupRequest(string Name);

public sealed class SaveCategoryGroupRequestValidator : AbstractValidator<SaveCategoryGroupRequest>
{
    public SaveCategoryGroupRequestValidator() =>
        RuleFor(x => x.Name).NotEmpty().WithMessage("اسم الصنف العام مطلوب").MaximumLength(100).WithMessage("الاسم طويل جدًا");
}

public interface ICategoryGroupService
{
    Task<IReadOnlyList<CategoryGroupDto>> ListAsync(CancellationToken ct = default);
    Task<CategoryGroupDto> CreateAsync(SaveCategoryGroupRequest request, CancellationToken ct = default);
    Task<CategoryGroupDto> RenameAsync(int id, SaveCategoryGroupRequest request, CancellationToken ct = default);
    /// <summary>Removes a general category; its categories stay, without one.</summary>
    Task DeleteAsync(int id, CancellationToken ct = default);
}

public sealed class CategoryGroupService(IAppDbContext db, IAuditService audit) : ICategoryGroupService
{
    public async Task<IReadOnlyList<CategoryGroupDto>> ListAsync(CancellationToken ct = default) =>
        await db.CategoryGroups.AsNoTracking().OrderBy(g => g.Name)
            .Select(g => new CategoryGroupDto(g.Id, g.Name, g.Categories.Count(), g.Categories.SelectMany(c => c.Products).Count(p => !p.IsArchived)))
            .ToListAsync(ct);

    private async Task<CategoryGroupDto> GetAsync(int id, CancellationToken ct) =>
        (await ListAsync(ct)).FirstOrDefault(g => g.Id == id) ?? throw new NotFoundException("الصنف العام غير موجود");

    public async Task<CategoryGroupDto> CreateAsync(SaveCategoryGroupRequest request, CancellationToken ct = default)
    {
        var group = new CategoryGroup { Name = Clean(request.Name) };
        await EnsureUniqueAsync(group, ct);
        db.CategoryGroups.Add(group);
        await db.SaveChangesAsync(ct);
        audit.Log("CategoryGroup.Create", "CategoryGroup", group.Id.ToString(), null, new { group.Name });
        await db.SaveChangesAsync(ct);
        return await GetAsync(group.Id, ct);
    }

    public async Task<CategoryGroupDto> RenameAsync(int id, SaveCategoryGroupRequest request, CancellationToken ct = default)
    {
        var group = await db.CategoryGroups.FirstOrDefaultAsync(g => g.Id == id, ct) ?? throw new NotFoundException("الصنف العام غير موجود");
        var before = group.Name;
        group.Name = Clean(request.Name);
        await EnsureUniqueAsync(group, ct);
        audit.Log("CategoryGroup.Update", "CategoryGroup", id.ToString(), new { Name = before }, new { group.Name });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var group = await db.CategoryGroups.FirstOrDefaultAsync(g => g.Id == id, ct) ?? throw new NotFoundException("الصنف العام غير موجود");
        // Its categories stay, without a general category.
        await db.Categories.Where(c => c.GroupId == id).ExecuteUpdateAsync(c => c.SetProperty(x => x.GroupId, (int?)null), ct);
        db.CategoryGroups.Remove(group);
        audit.Log("CategoryGroup.Delete", "CategoryGroup", id.ToString(), new { group.Name }, null);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private static string Clean(string name) => string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private async Task EnsureUniqueAsync(CategoryGroup group, CancellationToken ct)
    {
        if (await db.CategoryGroups.AnyAsync(g => g.Id != group.Id && g.Name == group.Name, ct))
            throw new ValidationFailedException("name", "يوجد صنف عام بنفس الاسم");
    }
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
        if (request.GroupId is { } groupId) query = query.Where(c => c.GroupId == groupId);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = ArabicText.Normalize(request.Search);
            var raw = request.Search.Trim();
            query = query.Where(c => c.NormalizedName.Contains(term) || c.Code.StartsWith(raw));
        }

        return await query.OrderByDescending(c => c.IsActive).ThenBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Code, c.Name, c.Description, c.IsActive, c.Products.Count(p => !p.IsArchived), c.GroupId, c.Group!.Name))
            .ToPagedAsync(request, ct);
    }

    public async Task<CategoryDto> GetAsync(int id, CancellationToken ct = default) =>
        await db.Categories.AsNoTracking().Where(c => c.Id == id)
            .Select(c => new CategoryDto(c.Id, c.Code, c.Name, c.Description, c.IsActive, c.Products.Count(p => !p.IsArchived), c.GroupId, c.Group!.Name))
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
        var before = new { category.Code, category.Name, category.Description, category.IsActive, category.GroupId };

        await ApplyAsync(category, request, ct);
        if (!string.IsNullOrWhiteSpace(request.Code)) category.Code = request.Code.Trim();
        await EnsureUniqueAsync(category, ct);

        audit.Log("Category.Update", "Category", id.ToString(), before, request);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    private async Task ApplyAsync(Category category, SaveCategoryRequest request, CancellationToken ct)
    {
        if (request.GroupId is { } groupId && !await db.CategoryGroups.AnyAsync(g => g.Id == groupId, ct))
            throw new ValidationFailedException("groupId", "الصنف العام غير موجود");
        category.GroupId = request.GroupId;
        // One space between words: «صنف  أ» and «صنف أ» are the same category.
        category.Name = string.Join(' ', request.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        category.NormalizedName = ArabicText.Normalize(category.Name);
        category.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        category.IsActive = request.IsActive;
    }

    private async Task EnsureUniqueAsync(Category category, CancellationToken ct)
    {
        if (await db.Categories.AnyAsync(c => c.Id != category.Id && c.Code == category.Code, ct))
            throw new ValidationFailedException("code", "كود الصنف مستخدم من قبل");
        if (await db.Categories.AnyAsync(c => c.Id != category.Id && c.Name == category.Name, ct))
            throw new ValidationFailedException("name", "يوجد صنف بنفس الاسم");
    }
}
