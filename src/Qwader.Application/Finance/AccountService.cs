using Microsoft.EntityFrameworkCore;
using Qwader.Application.Abstractions;
using Qwader.Application.Common;
using Qwader.Application.Ledgers;
using Qwader.Domain.Administration;
using Qwader.Domain.Finance;
using Qwader.Domain.Identity;

namespace Qwader.Application.Finance;

public interface IAccountService
{
    Task<IReadOnlyList<AccountDto>> ListAsync(bool includeInactive, CancellationToken ct = default);
    /// <summary>How much each account held on a day: at its start, what came in and went out, and at its end.</summary>
    Task<AccountsDayDto> DayAsync(DateOnly day, CancellationToken ct = default);
    Task<AccountDto> GetAsync(int id, CancellationToken ct = default);
    Task<AccountDto> CreateAsync(SaveAccountRequest request, CancellationToken ct = default);
    Task<AccountDto> UpdateAsync(int id, SaveAccountRequest request, CancellationToken ct = default);
    Task<AccountDto> SetOpeningBalanceAsync(int id, SetOpeningBalanceRequest request, CancellationToken ct = default);
    Task<AccountStatementDto> StatementAsync(int id, DateOnly? from, DateOnly? to, PagedRequest paging, CancellationToken ct = default);
    Task<PagedResult<AccountTransferDto>> TransfersAsync(PagedRequest request, CancellationToken ct = default);
    Task<AccountTransferDto> TransferAsync(CreateAccountTransferRequest request, CancellationToken ct = default);
    /// <summary>Cancels a transfer: the money goes back from the account that received it (it must still be there).</summary>
    Task<AccountTransferDto> VoidTransferAsync(int id, VoidDocumentRequest request, CancellationToken ct = default);
}

public sealed class AccountService(
    IAppDbContext db,
    IAccountLedger ledger,
    IDocumentNumberGenerator numbers,
    IIdempotencyService idempotency,
    ICurrentUser currentUser,
    IClock clock,
    IAuditService audit) : IAccountService
{
    private const string TransferOperation = "Account.Transfer";

    private bool CanSeeNumbers => currentUser.HasPermission(Permissions.AccountsViewNumbers);

    private static string? Mask(string? number) =>
        string.IsNullOrEmpty(number) ? null : number.Length <= 4 ? "••••" : "•••• " + number[^4..];

    public async Task<AccountsDayDto> DayAsync(DateOnly day, CancellationToken ct = default)
    {
        if (day > clock.LocalToday) throw new ValidationFailedException("date", "اختر يومًا حتى اليوم");
        var (start, end) = clock.LocalDaysToUtcRange(day, day);
        // Every movement counts, as in each account's statement: what was there when the day began, what moved during it.
        var before = await db.AccountTransactions.AsNoTracking().Where(t => t.Date < start).GroupBy(t => t.FinancialAccountId)
            .Select(g => new { Id = g.Key, Sum = g.Sum(t => t.Direction == MoneyDirection.In ? t.Amount : -t.Amount) }).ToDictionaryAsync(x => x.Id, x => x.Sum, ct);
        var during = await db.AccountTransactions.AsNoTracking().Where(t => t.Date >= start && t.Date < end).GroupBy(t => t.FinancialAccountId)
            .Select(g => new
            {
                Id = g.Key,
                In = g.Where(t => t.Direction == MoneyDirection.In).Sum(t => (decimal?)t.Amount) ?? 0,
                Out = g.Where(t => t.Direction == MoneyDirection.Out).Sum(t => (decimal?)t.Amount) ?? 0,
            }).ToDictionaryAsync(x => x.Id, ct);
        var accounts = await db.FinancialAccounts.AsNoTracking().OrderBy(a => a.Type).ThenBy(a => a.Id)
            .Select(a => new { a.Id, a.Name, a.Type, a.LogoFileId, a.IsActive }).ToListAsync(ct);
        var rows = accounts
            .Select(a =>
            {
                var opening = before.GetValueOrDefault(a.Id);
                var moved = during.GetValueOrDefault(a.Id);
                var @in = moved?.In ?? 0;
                var @out = moved?.Out ?? 0;
                return new AccountDayDto(a.Id, a.Name, a.Type, a.LogoFileId, a.IsActive, opening, @in, @out, opening + @in - @out);
            })
            // A disabled account is listed only if it held or moved money that day.
            .Where(r => r.IsActive || r.Opening != 0 || r.In != 0 || r.Out != 0)
            .ToList();
        // The same rule as the cash-flow report: a voided entry and its reversal are not cash flow, nor opening balances or transfers.
        var reversals = db.AccountTransactions.Where(r => r.Category == AccountTransactionCategory.Reversal);
        var moves = await db.AccountTransactions.AsNoTracking().Where(t => t.Date >= start && t.Date < end)
            .Select(t => new
            {
                Signed = t.Direction == MoneyDirection.In ? t.Amount : -t.Amount,
                t.Category,
                Voided = t.Category == AccountTransactionCategory.Reversal
                    || reversals.Any(r => r.SourceType == t.SourceType && r.SourceId == t.SourceId && r.FinancialAccountId == t.FinancialAccountId),
            }).ToListAsync(ct);
        var breakdown = new AccountsDayBreakdown(
            moves.Where(m => !m.Voided && m.Category is not (AccountTransactionCategory.OpeningBalance or AccountTransactionCategory.TransferIn or AccountTransactionCategory.TransferOut)).Sum(m => m.Signed),
            moves.Where(m => !m.Voided && m.Category == AccountTransactionCategory.OpeningBalance).Sum(m => m.Signed),
            moves.Where(m => m.Voided).Sum(m => m.Signed),
            moves.Where(m => !m.Voided && m.Category is AccountTransactionCategory.TransferIn or AccountTransactionCategory.TransferOut).Sum(m => m.Signed));
        return new AccountsDayDto(day, rows, rows.Sum(r => r.Opening), rows.Sum(r => r.In), rows.Sum(r => r.Out), rows.Sum(r => r.Closing), breakdown);
    }

    public async Task<IReadOnlyList<AccountDto>> ListAsync(bool includeInactive, CancellationToken ct = default)
    {
        var rows = await db.FinancialAccounts.AsNoTracking().Where(a => includeInactive || a.IsActive)
            .OrderByDescending(a => a.IsActive).ThenBy(a => a.Type).ThenBy(a => a.Id)
            .Select(a => new
            {
                Account = a,
                Pending = db.IncomingTransfers.Where(t => t.FinancialAccountId == a.Id && t.Status == TransferStatus.Pending).Sum(t => (decimal?)t.Amount) ?? 0,
            })
            .ToListAsync(ct);
        var showNumbers = CanSeeNumbers;
        return rows.Select(r => ToDto(r.Account, r.Pending, showNumbers)).ToList();
    }

    public async Task<AccountDto> GetAsync(int id, CancellationToken ct = default) =>
        (await ListAsync(true, ct)).FirstOrDefault(a => a.Id == id) ?? throw new NotFoundException("الحساب غير موجود");

    public async Task<AccountDto> CreateAsync(SaveAccountRequest request, CancellationToken ct = default)
    {
        var account = new FinancialAccount();
        await EnsureLogoAsync(request.LogoFileId, ct);
        Apply(account, request);
        await EnsureUniqueNameAsync(account, ct);
        db.FinancialAccounts.Add(account);
        audit.Log("Account.Create", "FinancialAccount", null, null, new { request.Name, request.Type, request.ProviderName });
        await db.SaveChangesAsync(ct);
        return await GetAsync(account.Id, ct);
    }

    public async Task<AccountDto> UpdateAsync(int id, SaveAccountRequest request, CancellationToken ct = default)
    {
        var account = await db.FinancialAccounts.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new NotFoundException("الحساب غير موجود");
        if (account.IsActive && !request.IsActive && account.CurrentBalance != 0)
            throw new BusinessRuleException($"لا يمكن تعطيل حساب رصيده {account.CurrentBalance:0.00} ₪؛ حوّل رصيده لحساب آخر أولًا");
        // A pending transfer could no longer be confirmed into a disabled account.
        if (account.IsActive && !request.IsActive
            && await db.IncomingTransfers.AnyAsync(t => t.FinancialAccountId == id && t.Status == Domain.Finance.TransferStatus.Pending, ct))
            throw new BusinessRuleException("على هذا الحساب حوالات بانتظار التحقق؛ أكّدها أو ارفضها أولًا");
        // Cash or not decides whether a till payment is a transfer to verify: fixed once money has moved.
        if (account.Type != request.Type && await db.AccountTransactions.AnyAsync(t => t.FinancialAccountId == id, ct))
            throw new ValidationFailedException("type", "لا يمكن تغيير نوع حساب عليه حركات");
        var before = new { account.Name, account.Type, account.ProviderName, account.HolderName, account.IsActive };
        // Users without the numbers permission see a masked value; sending it back must not overwrite the real number.
        var keepNumber = !CanSeeNumbers || request.AccountNumber == Mask(account.AccountNumber);
        var storedNumber = account.AccountNumber;
        await EnsureLogoAsync(request.LogoFileId, ct);
        Apply(account, request);
        if (keepNumber) account.AccountNumber = storedNumber;
        await EnsureUniqueNameAsync(account, ct);
        audit.Log("Account.Update", "FinancialAccount", id.ToString(), before, new { account.Name, account.Type, account.ProviderName, account.HolderName, account.IsActive });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<AccountDto> SetOpeningBalanceAsync(int id, SetOpeningBalanceRequest request, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var account = await db.FinancialAccounts.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new NotFoundException("الحساب غير موجود");
        var opening = request.OpeningBalance ?? throw new ValidationFailedException("openingBalance", "اكتب الرصيد الافتتاحي");
        var delta = opening - account.OpeningBalance;
        if (delta == 0) return await GetAsync(id, ct);

        // The balance only ever changes through a movement: the correction is posted, not written over.
        var description = "تعديل الرصيد الافتتاحي" + (string.IsNullOrWhiteSpace(request.Notes) ? "" : $" — {request.Notes.Trim()}");
        // Lowering it takes money out: refused when the account no longer holds it (an account never goes below zero).
        await ledger.PostAsync(id, delta > 0 ? MoneyDirection.In : MoneyDirection.Out, Math.Abs(delta), AccountTransactionCategory.OpeningBalance,
            new LedgerSource("FinancialAccount", id, null, description), clock.UtcNow, ct, requireFunds: delta < 0);
        await db.Database.ExecuteSqlAsync($"UPDATE FinancialAccounts SET OpeningBalance = {opening} WHERE Id = {id}", ct);

        audit.Log("Account.SetOpeningBalance", "FinancialAccount", id.ToString(), new { account.OpeningBalance }, new { OpeningBalance = opening }, request.Notes);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<AccountStatementDto> StatementAsync(int id, DateOnly? from, DateOnly? to, PagedRequest paging, CancellationToken ct = default)
    {
        var account = await db.FinancialAccounts.AsNoTracking().Where(a => a.Id == id).Select(a => new { a.Id, a.Name }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("الحساب غير موجود");
        var end = to ?? clock.LocalToday;
        var (startUtc, endUtc) = clock.LocalDaysToUtcRange(from ?? end, end);
        // No start day: every movement up to the end, however old its date, so the closing balance is the account's.
        if (from is null) startUtc = DateTime.MinValue;
        var all = db.AccountTransactions.AsNoTracking().Where(t => t.FinancialAccountId == id);

        var opening = from is null ? 0 : await all.Where(t => t.Date < startUtc)
            .SumAsync(t => t.Direction == MoneyDirection.In ? t.Amount : -t.Amount, ct);
        var period = all.Where(t => t.Date >= startUtc && t.Date < endUtc);
        var totalIn = await period.Where(t => t.Direction == MoneyDirection.In).SumAsync(t => (decimal?)t.Amount, ct) ?? 0;
        var totalOut = await period.Where(t => t.Direction == MoneyDirection.Out).SumAsync(t => (decimal?)t.Amount, ct) ?? 0;

        var users = db.Users;
        // By date (newest first), so an entry dated an earlier day sits on its day; each line's balance is worked out in that
        // same order from the period's closing balance, not the balance stored when the entry was made.
        var page = await period.OrderByDescending(t => t.Date).ThenByDescending(t => t.Id)
            .Select(t => new { t.Id, t.Date, t.Direction, t.Amount, t.Category, t.SourceType, t.SourceId, t.SourceNumber, t.Description,
                UserName = users.Where(u => u.Id == t.UserId).Select(u => u.FullName).FirstOrDefault() })
            .ToPagedAsync(paging, ct);

        var closing = opening + totalIn - totalOut;
        var balance = closing;
        if (page.Items.Count > 0)
        {
            // Entries newer than this page's first one (on earlier pages) are taken off the closing balance first.
            var first = page.Items[0];
            balance -= await period.Where(t => t.Date > first.Date || (t.Date == first.Date && t.Id > first.Id))
                .SumAsync(t => (decimal?)(t.Direction == MoneyDirection.In ? t.Amount : -t.Amount), ct) ?? 0;
        }
        var lines = new List<AccountMovementDto>(page.Items.Count);
        foreach (var t in page.Items)
        {
            lines.Add(new AccountMovementDto(t.Id, t.Date, t.Direction, t.Amount, balance, t.Category,
                FinanceNames.Category(t.Category), t.SourceType, t.SourceId, t.SourceNumber, t.Description, t.UserName));
            balance -= t.Direction == MoneyDirection.In ? t.Amount : -t.Amount;
        }
        var movements = new PagedResult<AccountMovementDto>(lines, page.Page, page.PageSize, page.TotalCount);

        return new AccountStatementDto(account.Id, account.Name, from, end, opening, totalIn, totalOut, closing, movements);
    }

    public async Task<PagedResult<AccountTransferDto>> TransfersAsync(PagedRequest request, CancellationToken ct = default)
    {
        var users = db.Users;
        return await db.AccountTransfers.AsNoTracking().OrderByDescending(t => t.Id)
            .Select(t => new AccountTransferDto(t.Id, t.Number, t.Date, t.FromAccountId, t.FromAccount.Name, t.ToAccountId, t.ToAccount.Name,
                t.Amount, t.Notes, users.Where(u => u.Id == t.CreatedById).Select(u => u.FullName).FirstOrDefault(), t.VoidedAt, t.VoidReason))
            .ToPagedAsync(request, ct);
    }

    public async Task<AccountTransferDto> VoidTransferAsync(int id, VoidDocumentRequest request, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.LockRowAsync("AccountTransfers", id, ct);
        var transfer = await db.AccountTransfers.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("التحويل غير موجود");
        if (transfer.VoidedAt is not null) throw new BusinessRuleException("هذا التحويل ملغى من قبل");

        var now = clock.UtcNow;
        var reason = request.Reason.Trim();
        // Paired with the transfer's own entries (same source) so reports leave both out; the receiving account must still hold it.
        var source = new LedgerSource("AccountTransfer", transfer.Id, transfer.Number, $"إلغاء التحويل: {reason}");
        foreach (var (accountId, direction) in new[] { (transfer.ToAccountId, MoneyDirection.Out), (transfer.FromAccountId, MoneyDirection.In) }.OrderBy(x => x.Item1))
            await ledger.PostAsync(accountId, direction, transfer.Amount, AccountTransactionCategory.Reversal, source, now, ct,
                requireFunds: direction == MoneyDirection.Out);

        transfer.VoidedAt = now;
        transfer.VoidedById = currentUser.UserId;
        transfer.VoidReason = reason;
        audit.Log("Account.TransferVoid", "AccountTransfer", transfer.Id.ToString(), null, new { transfer.Number, transfer.Amount }, reason);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await TransfersAsync(new PagedRequest { PageSize = 100 }, ct)).Items.First(t => t.Id == transfer.Id);
    }

    public async Task<AccountTransferDto> TransferAsync(CreateAccountTransferRequest request, CancellationToken ct = default)
    {
        if (await idempotency.FindAsync(request.IdempotencyKey, TransferOperation, ct) is { } done)
            return (await TransfersAsync(new PagedRequest { PageSize = 100 }, ct)).Items.First(t => t.Id == int.Parse(done.ResultId!));

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = clock.UtcNow;
        var transfer = new AccountTransfer
        {
            Number = await numbers.NextAsync(DocumentType.AccountTransfer, ct),
            FromAccountId = request.FromAccountId,
            ToAccountId = request.ToAccountId,
            Amount = request.Amount,
            Date = now,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
        };
        db.AccountTransfers.Add(transfer);
        await db.SaveChangesAsync(ct);

        // Lock the two accounts in id order so two opposite transfers cannot deadlock.
        var source = new LedgerSource("AccountTransfer", transfer.Id, transfer.Number, transfer.Notes);
        foreach (var (accountId, direction) in new[] { (request.FromAccountId, MoneyDirection.Out), (request.ToAccountId, MoneyDirection.In) }.OrderBy(x => x.Item1))
            await ledger.PostAsync(accountId, direction, request.Amount,
                direction == MoneyDirection.Out ? AccountTransactionCategory.TransferOut : AccountTransactionCategory.TransferIn,
                source, now, ct, requireFunds: direction == MoneyDirection.Out);

        idempotency.Record(request.IdempotencyKey, TransferOperation, "AccountTransfer", transfer.Id.ToString());
        audit.Log("Account.Transfer", "AccountTransfer", transfer.Id.ToString(), null, new { transfer.Number, request.FromAccountId, request.ToAccountId, request.Amount });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await TransfersAsync(new PagedRequest { PageSize = 5 }, ct)).Items.First(t => t.Id == transfer.Id);
    }

    private static void Apply(FinancialAccount account, SaveAccountRequest r)
    {
        account.Name = r.Name.Trim();
        account.Type = r.Type;
        account.ProviderName = string.IsNullOrWhiteSpace(r.ProviderName) ? null : r.ProviderName.Trim();
        account.HolderName = string.IsNullOrWhiteSpace(r.HolderName) ? null : r.HolderName.Trim();
        account.AccountNumber = string.IsNullOrWhiteSpace(r.AccountNumber) ? null : r.AccountNumber.Trim();
        account.IsActive = r.IsActive;
        account.LogoFileId = r.LogoFileId;
    }

    /// <summary>The logo must be an uploaded account logo (not some other file of the store).</summary>
    private async Task EnsureLogoAsync(Guid? fileId, CancellationToken ct)
    {
        if (fileId is { } id && !await db.StoredFiles.AnyAsync(f => f.Id == id && f.Purpose == Domain.Files.FilePurpose.AccountLogo, ct))
            throw new ValidationFailedException("logoFileId", "الصورة غير موجودة، أعد رفعها");
    }

    private async Task EnsureUniqueNameAsync(FinancialAccount account, CancellationToken ct)
    {
        if (await db.FinancialAccounts.AnyAsync(a => a.Id != account.Id && a.Name == account.Name, ct))
            throw new ValidationFailedException("name", "يوجد حساب بنفس الاسم");
    }

    private static AccountDto ToDto(FinancialAccount a, decimal pending, bool showNumbers) =>
        new(a.Id, a.Name, a.Type, a.ProviderName, a.HolderName, showNumbers ? a.AccountNumber : Mask(a.AccountNumber),
            a.OpeningBalance, a.CurrentBalance, a.IsActive, pending, a.LogoFileId);
}
