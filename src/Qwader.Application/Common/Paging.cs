using Microsoft.EntityFrameworkCore;

namespace Qwader.Application.Common;

public class PagedRequest
{
    private int _page = 1;
    private int _pageSize = 20;

    public int Page { get => _page; set => _page = Math.Max(1, value); }
    public int PageSize { get => _pageSize; set => _pageSize = Math.Clamp(value, 1, 100); }
    public string? Search { get; set; }
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public static class PagingExtensions
{
    public static async Task<PagedResult<T>> ToPagedAsync<T>(this IQueryable<T> query, PagedRequest request, CancellationToken ct = default)
    {
        var total = await query.CountAsync(ct);
        var items = await query.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct);
        return new PagedResult<T>(items, request.Page, request.PageSize, total);
    }
}
