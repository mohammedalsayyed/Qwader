using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Qwader.Application.Abstractions;
using Qwader.Application.Common;
using Qwader.Application.Search;
using Qwader.Domain.Files;
using Qwader.Domain.Identity;

namespace Qwader.Api.Controllers;

[ApiController]
[Route("api/search")]
public sealed class SearchController(ISearchService search) : ControllerBase
{
    [HttpGet]
    public Task<SearchResult> Search([FromQuery] string q, [FromQuery] int perKind = 6, CancellationToken ct = default) =>
        search.SearchAsync(q ?? "", perKind, ct);
}

[ApiController]
[Route("api/notifications")]
public sealed class NotificationsController(ISearchService search) : ControllerBase
{
    [HttpGet]
    public Task<NotificationsDto> Get(CancellationToken ct) => search.NotificationsAsync(ct);
}

public sealed record UploadedFileDto(Guid Id, string Url, string ContentType, long Size);

[ApiController]
[Route("api/files")]
public sealed class FilesController(IFileStorage storage, IAppDbContext db, ICurrentUser currentUser) : ControllerBase
{
    private static readonly Dictionary<FilePurpose, string[]> UploadPermissions = new()
    {
        [FilePurpose.ProductImage] = [Permissions.ProductsManage],
        [FilePurpose.TransferProof] = [Permissions.TransfersCreate],
        [FilePurpose.VoucherAttachment] = [Permissions.VouchersCreate],
        [FilePurpose.ExpenseAttachment] = [Permissions.ExpensesManage],
    };

    [HttpPost]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<UploadedFileDto> Upload([FromQuery] FilePurpose purpose, IFormFile file, CancellationToken ct)
    {
        if (!UploadPermissions.TryGetValue(purpose, out var required) || !required.Any(currentUser.HasPermission))
            throw new ForbiddenException("ليست لديك صلاحية رفع هذا النوع من الملفات");

        await using var stream = file.OpenReadStream();
        var stored = await storage.SaveAsync(new FileUpload(stream, file.FileName, file.ContentType, file.Length), purpose, ct);
        await db.SaveChangesAsync(ct);
        return new UploadedFileDto(stored.Id, $"/api/files/{stored.Id}", stored.ContentType, stored.Size);
    }

    /// <summary>
    /// Product images are public by unguessable id so &lt;img&gt; tags work without a token;
    /// every other file (transfer proofs, attachments) requires a signed-in user.
    /// </summary>
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var opened = await storage.OpenAsync(id, ct);
        if (opened is not { } found) return NotFound();
        if (found.File.Purpose != FilePurpose.ProductImage && User.Identity?.IsAuthenticated != true)
        {
            await found.Content.DisposeAsync();
            return Unauthorized();
        }

        Response.Headers.CacheControl = found.File.Purpose == FilePurpose.ProductImage ? "public, max-age=31536000, immutable" : "private, no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(found.Content, found.File.ContentType);
    }
}
