using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Qwader.Application.Abstractions;
using Qwader.Application.Common;
using Qwader.Application.Settings;
using Qwader.Domain.Administration;
using Qwader.Domain.Files;
using Qwader.Domain.Identity;
using Qwader.Infrastructure.Persistence;
using Qwader.Infrastructure.Persistence.Seed;

namespace Qwader.Infrastructure.Services;

public sealed class SystemResetService(
    AppDbContext db,
    UserManager<AppUser> userManager,
    ICurrentUser currentUser,
    IFileStorage files,
    IAuditService audit,
    IClock clock,
    ISystemBackupService backup,
    Microsoft.Extensions.Configuration.IConfiguration config,
    Microsoft.Extensions.Hosting.IHostEnvironment env) : ISystemResetService
{
    private const int BackupsKept = 10;

    /// <summary>
    /// A full backup on the server before anything is wiped, so a reset by mistake can be undone. If it cannot be written,
    /// nothing is reset. The newest ten are kept.
    /// </summary>
    private async Task<string> BackupBeforeResetAsync(CancellationToken ct)
    {
        var folder = Path.GetFullPath(config["Backups:Root"] ?? Path.Combine(env.ContentRootPath, "App_Data", "backups"));
        Directory.CreateDirectory(folder);
        var name = "before-reset-" + backup.FileName().Replace("qwader-backup-", "");
        var path = Path.Combine(folder, name);
        await using (var file = File.Create(path)) await backup.WriteAsync(file, ct);
        foreach (var old in new DirectoryInfo(folder).GetFiles("before-reset-*.zip").OrderByDescending(f => f.CreationTimeUtc).Skip(BackupsKept))
            old.Delete();
        return name;
    }

    public async Task<int> DeleteAuditLogAsync(DeleteAuditLogRequest request, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(currentUser.RequiredUserId.ToString()) ?? throw new UnauthorizedException();
        if (!await userManager.IsInRoleAsync(user, SystemRoles.Admin))
            throw new ForbiddenException("حذف سجل التدقيق متاح لمدير النظام فقط");
        if (!await userManager.CheckPasswordAsync(user, request.Password))
            throw new ValidationFailedException("password", "كلمة المرور غير صحيحة");

        var (start, end) = clock.LocalDaysToUtcRange(request.From, request.To);
        var deleted = await db.AuditLogs.Where(a => a.OccurredAt >= start && a.OccurredAt < end).ExecuteDeleteAsync(ct);
        // Written after the delete, so this entry stays: the log always shows that a period was removed and by whom.
        audit.Log("AuditLog.Delete", "AuditLog", null, null, new { From = request.From, To = request.To, Deleted = deleted });
        await db.SaveChangesAsync(ct);
        return deleted;
    }

    public async Task<SystemResetResult> ResetAsync(SystemResetRequest request, CancellationToken ct = default)
    {
        // A copy in daily use can switch the reset off altogether (System:AllowReset = false in its settings). Checked first, so
        // whether it is off can be confirmed safely: any request, even with a wrong password, gets this answer and nothing more.
        if (string.Equals(config["System:AllowReset"], "false", StringComparison.OrdinalIgnoreCase))
            throw new ForbiddenException("تصفير النظام معطّل في هذه النسخة");
        var user = await userManager.FindByIdAsync(currentUser.RequiredUserId.ToString()) ?? throw new UnauthorizedException();
        if (!await userManager.IsInRoleAsync(user, SystemRoles.Admin))
            throw new ForbiddenException("تصفير النظام متاح لمدير النظام فقط");
        if (!await userManager.CheckPasswordAsync(user, request.Password))
            throw new ValidationFailedException("password", "كلمة المرور غير صحيحة");

        var backupFile = await BackupBeforeResetAsync(ct);
        var everything = request.Mode == SystemResetMode.Everything;
        var deleted = 0;
        // Whole-table deletes right after a bulk import can run on stale statistics and exceed the default 30 s;
        // this is a rare, all-or-nothing admin operation, so give it room rather than fail half-way and roll back.
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
        List<string> orphanedFiles;

        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            // Documents first (children before parents); a transfer and its receipt voucher point at each other, so unlink them first.
            await db.IncomingTransfers.ExecuteUpdateAsync(s => s.SetProperty(t => t.ReceiptVoucherId, (int?)null), ct);
            deleted += await db.ReceiptAllocations.ExecuteDeleteAsync(ct);
            deleted += await db.UserNotifications.ExecuteDeleteAsync(ct); // they point at invoices and transfers that are gone
            deleted += await db.PayrollEntries.ExecuteDeleteAsync(ct);
            deleted += await db.PayrollSettlements.ExecuteDeleteAsync(ct);
            deleted += await db.Expenses.ExecuteDeleteAsync(ct);
            deleted += await db.PaymentVouchers.ExecuteDeleteAsync(ct);
            deleted += await db.ReceiptVouchers.ExecuteDeleteAsync(ct);
            deleted += await db.IncomingTransfers.ExecuteDeleteAsync(ct);
            deleted += await db.AccountTransfers.ExecuteDeleteAsync(ct);

            deleted += await db.SaleReturnLines.ExecuteDeleteAsync(ct);
            deleted += await db.SaleReturns.ExecuteDeleteAsync(ct);
            deleted += await db.SalePayments.ExecuteDeleteAsync(ct);
            deleted += await db.SaleInvoiceLines.ExecuteDeleteAsync(ct);
            deleted += await db.SaleInvoices.ExecuteDeleteAsync(ct);
            deleted += await db.CustomerLedgerEntries.ExecuteDeleteAsync(ct);

            deleted += await db.PurchaseReturnLines.ExecuteDeleteAsync(ct);
            deleted += await db.PurchaseReturns.ExecuteDeleteAsync(ct);
            deleted += await db.PurchaseInvoiceLines.ExecuteDeleteAsync(ct);
            deleted += await db.PurchaseInvoices.ExecuteDeleteAsync(ct);
            deleted += await db.SupplierLedgerEntries.ExecuteDeleteAsync(ct);

            deleted += await db.StockCountLines.ExecuteDeleteAsync(ct);
            deleted += await db.StockCounts.ExecuteDeleteAsync(ct);
            deleted += await db.StockMovements.ExecuteDeleteAsync(ct);
            deleted += await db.StockBalances.ExecuteDeleteAsync(ct);
            deleted += await db.AccountTransactions.ExecuteDeleteAsync(ct);
            await db.IdempotencyRecords.ExecuteDeleteAsync(ct);

            if (everything)
            {
                // Off the payroll too (their users stay and can be added again).
                deleted += await db.Employees.ExecuteDeleteAsync(ct);
                deleted += await db.Products.ExecuteDeleteAsync(ct);
                deleted += await db.Brands.ExecuteDeleteAsync(ct);
                deleted += await db.Categories.ExecuteDeleteAsync(ct);
                deleted += await db.Customers.Where(c => !c.IsWalkIn).ExecuteDeleteAsync(ct);
                deleted += await db.Suppliers.ExecuteDeleteAsync(ct);
                deleted += await db.FinancialAccounts.ExecuteDeleteAsync(ct);
                await db.DocumentSequences.ExecuteDeleteAsync(ct);
            }
            else
            {
                // Kept records start again from zero: no stock, no debts, empty accounts. Codes (P-, C-, S-, CAT-) keep counting.
                await db.Products.ExecuteUpdateAsync(s => s.SetProperty(p => p.AverageCost, 0m), ct);
                await db.FinancialAccounts.ExecuteUpdateAsync(s => s.SetProperty(a => a.OpeningBalance, 0m).SetProperty(a => a.CurrentBalance, 0m), ct);
                await db.Suppliers.ExecuteUpdateAsync(s => s.SetProperty(x => x.Balance, 0m), ct);
                await db.DocumentSequences.Where(s => s.DocumentType < DocumentType.Customer).ExecuteDeleteAsync(ct);
            }
            await db.Customers.ExecuteUpdateAsync(s => s.SetProperty(c => c.Balance, 0m), ct);

            // Attachments of deleted documents (and, on a full reset, product images) lose their owner.
            var unowned = db.StoredFiles.Where(f => everything || f.Purpose != FilePurpose.ProductImage);
            orphanedFiles = await unowned.Select(f => f.StoragePath).ToListAsync(ct);
            await unowned.ExecuteDeleteAsync(ct);

            if (everything) await DbInitializer.SeedReferenceDataAsync(db, ct);

            audit.Log("System.Reset", "System", null, null, new { Mode = request.Mode.ToString(), DeletedRows = deleted, Backup = backupFile },
                everything ? "مسح كل البيانات" : "مسح الحركات فقط");
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        // Bytes go only after the commit; a leftover file is harmless, a missing one for a live row is not.
        foreach (var path in orphanedFiles)
        {
            try { files.DeleteContent(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        db.ChangeTracker.Clear();
        return new SystemResetResult(request.Mode, deleted, backupFile);
    }
}
