using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Qwader.Application.Abstractions;
using Qwader.Application.Common;
using Qwader.Domain.Files;
using Qwader.Infrastructure.Persistence;

namespace Qwader.Infrastructure.Services;

/// <summary>
/// Stores uploads on local disk outside wwwroot (Storage:Root, default App_Data/uploads) under random names.
/// The type is decided from the file's leading bytes, never from its name or the client's content type.
/// </summary>
public sealed class LocalFileStorage(AppDbContext db, ICurrentUser currentUser, IClock clock, IConfiguration config, IHostEnvironment env) : IFileStorage
{
    public const long MaxBytes = 5 * 1024 * 1024;

    private string Root => Path.GetFullPath(config["Storage:Root"] ?? Path.Combine(env.ContentRootPath, "App_Data", "uploads"));

    public async Task<StoredFile> SaveAsync(FileUpload upload, FilePurpose purpose, CancellationToken ct = default)
    {
        if (upload.Length <= 0) throw new ValidationFailedException("file", "الملف فارغ");
        if (upload.Length > MaxBytes) throw new ValidationFailedException("file", "حجم الملف أكبر من 5 ميغابايت");

        await using var buffer = new MemoryStream();
        await upload.Content.CopyToAsync(buffer, ct);
        if (buffer.Length > MaxBytes) throw new ValidationFailedException("file", "حجم الملف أكبر من 5 ميغابايت");

        var (contentType, extension) = Sniff(buffer.GetBuffer().AsSpan(0, (int)Math.Min(buffer.Length, 16)));
        if (contentType is null) throw new ValidationFailedException("file", "نوع الملف غير مدعوم. المسموح: صور JPG أو PNG أو WEBP، أو PDF");
        if (purpose == FilePurpose.ProductImage && contentType == "application/pdf")
            throw new ValidationFailedException("file", "صورة المنتج يجب أن تكون صورة وليست PDF");

        var now = clock.UtcNow;
        var id = Guid.NewGuid();
        var relative = Path.Combine(now.ToString("yyyy"), now.ToString("MM"), id.ToString("N") + extension);
        var fullPath = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        buffer.Position = 0;
        await using (var file = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write))
            await buffer.CopyToAsync(file, ct);

        var stored = new StoredFile
        {
            Id = id,
            Purpose = purpose,
            OriginalName = Path.GetFileName(upload.FileName).Length > 200 ? Path.GetFileName(upload.FileName)[..200] : Path.GetFileName(upload.FileName),
            ContentType = contentType,
            Size = buffer.Length,
            StoragePath = relative,
            UploadedAt = now,
            UploadedById = currentUser.UserId,
        };
        db.StoredFiles.Add(stored);
        return stored;
    }

    public async Task<(StoredFile File, Stream Content)?> OpenAsync(Guid id, CancellationToken ct = default)
    {
        var file = await db.StoredFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
        if (file is null) return null;

        var fullPath = Path.GetFullPath(Path.Combine(Root, file.StoragePath));
        if (!fullPath.StartsWith(Root, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath)) return null;
        return (file, new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read));
    }

    public void DeleteContent(string storagePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(Root, storagePath));
        if (fullPath.StartsWith(Root, StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath)) File.Delete(fullPath);
    }

    private static (string? ContentType, string Extension) Sniff(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF) return ("image/jpeg", ".jpg");
        if (head.Length >= 8 && head[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return ("image/png", ".png");
        if (head.Length >= 12 && head[..4].SequenceEqual("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8)) return ("image/webp", ".webp");
        if (head.Length >= 5 && head[..5].SequenceEqual("%PDF-"u8)) return ("application/pdf", ".pdf");
        return (null, "");
    }
}
