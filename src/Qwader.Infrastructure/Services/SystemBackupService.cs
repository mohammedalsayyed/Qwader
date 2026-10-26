using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Qwader.Application.Abstractions;
using Qwader.Application.Common;
using Qwader.Application.Settings;
using Qwader.Domain.Administration;
using Qwader.Domain.Identity;
using Qwader.Domain.Notifications;
using Qwader.Infrastructure.Persistence;

namespace Qwader.Infrastructure.Services;

/// <summary>
/// A downloadable copy of the store. The hosting gives no access to SQL Server's own BACKUP, so this is a logical backup:
/// every table as JSON (enough to rebuild the database), the main lists as Excel for reading, and the uploaded files.
/// </summary>
public sealed class SystemBackupService(
    AppDbContext db,
    UserManager<AppUser> userManager,
    ICurrentUser currentUser,
    IClock clock,
    IExcelService excel,
    IFileStorage files,
    IConfiguration config,
    IAuditService audit) : ISystemBackupService
{
    /// <summary>Sign-in state and per-device data: worthless in a backup and sensitive.</summary>
    private static readonly HashSet<Type> Skipped =
    [
        typeof(RefreshToken), typeof(IdempotencyRecord), typeof(PushSubscription),
        typeof(IdentityUserClaim<int>), typeof(IdentityUserLogin<int>), typeof(IdentityUserToken<int>), typeof(IdentityRoleClaim<int>),
    ];

    private static readonly HashSet<string> SecretColumns = ["PasswordHash", "SecurityStamp", "ConcurrencyStamp"];

    private static readonly JsonWriterOptions JsonOptions = new() { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) };

    public string FileName() => $"qwader-backup-{clock.ToLocal(clock.UtcNow):yyyy-MM-dd-HHmm}.zip";

    public async Task WriteAsync(Stream output, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(currentUser.RequiredUserId.ToString()) ?? throw new UnauthorizedException();
        if (!await userManager.IsInRoleAsync(user, SystemRoles.Admin))
            throw new ForbiddenException("النسخ الاحتياطي متاح لمدير النظام فقط");

        // Big stores take a while to read; this is a rare admin action.
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
        var counts = new List<(string Table, int Rows)>();

        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8))
        {
            var dump = typeof(SystemBackupService).GetMethod(nameof(DumpTableAsync), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            foreach (var entity in db.Model.GetEntityTypes()
                         .Where(e => !e.IsOwned() && !e.HasSharedClrType && !Skipped.Contains(e.ClrType) && e.GetTableName() is not null)
                         .OrderBy(e => e.GetTableName()))
            {
                var rows = await (Task<int>)dump.MakeGenericMethod(entity.ClrType).Invoke(this, [zip, entity, ct])!;
                counts.Add((entity.GetTableName()!, rows));
            }

            await WriteSheetsAsync(zip, ct);
            var fileCount = await WriteFilesAsync(zip, ct);
            await WriteReadmeAsync(zip, counts, fileCount);
        }

        audit.Log("System.Backup", "System", null, null, new { Tables = counts.Count, Rows = counts.Sum(c => c.Rows) });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>One table → data/«table».json: an array of rows with every column (no navigation properties).</summary>
    private async Task<int> DumpTableAsync<T>(ZipArchive zip, IEntityType entity, CancellationToken ct) where T : class
    {
        var columns = entity.GetProperties().Where(p => p.PropertyInfo is not null && !SecretColumns.Contains(p.Name)).ToList();
        var entry = zip.CreateEntry($"data/{entity.GetTableName()}.json", CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await using var json = new Utf8JsonWriter(stream, JsonOptions);
        json.WriteStartArray();
        var rows = 0;
        await foreach (var row in db.Set<T>().AsNoTracking().AsAsyncEnumerable().WithCancellation(ct))
        {
            // The push signing key is a secret: a restored store simply makes a new one.
            if (row is Setting s && s.Key.StartsWith("Push.Vapid", StringComparison.Ordinal)) continue;
            json.WriteStartObject();
            foreach (var column in columns)
            {
                json.WritePropertyName(column.Name);
                JsonSerializer.Serialize(json, column.PropertyInfo!.GetValue(row), column.ClrType);
            }
            json.WriteEndObject();
            if (++rows % 500 == 0) await json.FlushAsync(ct);
        }
        json.WriteEndArray();
        await json.FlushAsync(ct);
        return rows;
    }

    /// <summary>The lists people look at, as Excel: products with stock and prices, customers, suppliers, accounts.</summary>
    private async Task WriteSheetsAsync(ZipArchive zip, CancellationToken ct)
    {
        var products = await db.Products.AsNoTracking().OrderBy(p => p.Category.Name).ThenBy(p => p.Name)
            .Select(p => new object?[] { p.Sku, p.Barcode, p.Name, p.Category.Name, p.Unit.Name, p.PurchasePrice, p.AverageCost, p.WholesalePrice,
                p.RetailPrice, p.MinSalePrice, p.StockBalances.Sum(b => b.Quantity), p.IsArchived ? "مؤرشف" : "" })
            .ToListAsync(ct);
        await AddAsync(zip, "excel/المنتجات.xlsx", excel.Write("المنتجات",
            [new("الكود", 12), new("الباركود", 16), new("اسم المنتج", 34), new("الصنف", 22), new("الوحدة", 9), new("سعر الشراء", 11), new("متوسط التكلفة", 12),
             new("سعر الجملة", 11), new("سعر المفرق", 11), new("أقل سعر بيع", 11), new("الكمية", 10), new("الحالة", 10)], products));

        var customers = await db.Customers.AsNoTracking().Where(c => !c.IsWalkIn).OrderBy(c => c.Name)
            .Select(c => new object?[] { c.Code, c.Name, c.Phone, c.Address, c.Balance }).ToListAsync(ct);
        await AddAsync(zip, "excel/العملاء.xlsx", excel.Write("العملاء",
            [new("الكود", 10), new("الاسم", 28), new("الجوال", 14), new("العنوان", 24), new("الرصيد (عليه +)", 14)], customers));

        var suppliers = await db.Suppliers.AsNoTracking().OrderBy(s => s.Name)
            .Select(s => new object?[] { s.Code, s.Name, s.Phone, s.Balance }).ToListAsync(ct);
        await AddAsync(zip, "excel/الموردون.xlsx", excel.Write("الموردون",
            [new("الكود", 10), new("الاسم", 28), new("الجوال", 14), new("المستحق له", 14)], suppliers));

        var accounts = await db.FinancialAccounts.AsNoTracking().OrderBy(a => a.Id)
            .Select(a => new object?[] { a.Name, a.ProviderName, a.AccountNumber, a.CurrentBalance }).ToListAsync(ct);
        await AddAsync(zip, "excel/الحسابات.xlsx", excel.Write("الحسابات",
            [new("الحساب", 22), new("البنك / المزود", 18), new("رقم الحساب", 18), new("الرصيد", 14)], accounts));
    }

    /// <summary>Uploaded photos and attachments, under the same relative paths the StoredFiles table records.</summary>
    private async Task<int> WriteFilesAsync(ZipArchive zip, CancellationToken ct)
    {
        var stored = await db.StoredFiles.AsNoTracking().Select(f => new { f.Id, f.StoragePath }).ToListAsync(ct);
        var written = 0;
        foreach (var f in stored)
        {
            if (await files.OpenAsync(f.Id, ct) is not { } opened) continue; // a file missing on disk is skipped, not fatal
            await using var content = opened.Content;
            var entry = zip.CreateEntry("uploads/" + f.StoragePath.Replace('\\', '/'), CompressionLevel.NoCompression); // images are compressed already
            await using var target = entry.Open();
            await content.CopyToAsync(target, ct);
            written++;
        }
        return written;
    }

    private async Task WriteReadmeAsync(ZipArchive zip, List<(string Table, int Rows)> counts, int fileCount)
    {
        var text = new StringBuilder()
            .AppendLine("نسخة احتياطية — معرض قويدر")
            .AppendLine($"التاريخ: {clock.ToLocal(clock.UtcNow):yyyy-MM-dd HH:mm} (توقيت فلسطين)")
            .AppendLine($"إصدار النظام: {config["App:Version"] ?? "—"}")
            .AppendLine()
            .AppendLine("المحتوى:")
            .AppendLine("  data/     كل جداول قاعدة البيانات بصيغة JSON (تكفي لإعادة بناء البيانات).")
            .AppendLine("  excel/    المنتجات والعملاء والموردون والحسابات بصيغة Excel للاطلاع.")
            .AppendLine($"  uploads/  صور المنتجات ومرفقات الحوالات والسندات ({fileCount} ملف).")
            .AppendLine()
            .AppendLine("لا تحتوي النسخة على كلمات المرور ولا جلسات الدخول ولا مفاتيح الإشعارات.")
            .AppendLine("احفظها في مكان آمن: فيها أسماء العملاء وأرقام هواتفهم وأرصدتهم.")
            .AppendLine()
            .AppendLine("عدد الصفوف في كل جدول:");
        foreach (var (table, rows) in counts) text.AppendLine($"  {table}: {rows}");
        await AddAsync(zip, "اقرأني.txt", Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text.ToString())).ToArray());
    }

    private static async Task AddAsync(ZipArchive zip, string name, byte[] bytes)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await stream.WriteAsync(bytes);
    }
}
