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
    Task<AccountDto> GetAsync(int id, CancellationToken ct = default);
    Task<AccountDto> CreateAsync(SaveAccountRequest request, CancellationToken ct = default);
    Task<AccountDto> UpdateAsync(int id, SaveAccountRequest request, CancellationToken ct = default);
    Task<AccountDto> SetOpeningBalanceAsync(int id, SetOpeningBalanceRequest request, CancellationToken ct = default);
    Task<AccountStatementDto> StatementAsync(int id, DateOnly? from, DateOnly? to, PagedRequest paging, CancellationToken ct = default);
    Task<PagedResult<AccountTransferDto>> TransfersAsync(PagedRequest request, CancellationToken ct = default);
    Task<AccountTransferDto> TransferAsync(CreateAccountTransferRequest request, CancellationToken ct = default);
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
        var before = new { account.Name, account.Type, account.ProviderName, account.HolderName, account.IsActive };
        // Users without the numbers permission see a masked value; sending it back must not overwrite the real number.
        var keepNumber = !CanSeeNumbers || request.AccountNumber == Mask(account.AccountNumber);
        var storedNumber = account.AccountNumber;
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
        var delta = request.OpeningBalance - account.OpeningBalance;
        if (delta == 0) return await GetAsync(id, ct);

        // The balance only ever changes through a movement: the correction is posted, not written over.
        var description = "تعديل الرصيد الافتتاحي" + (string.IsNullOrWhiteSpace(request.Notes) ? "" : $" — {request.Notes.Trim()}");
        await ledger.PostAsync(id, delta > 0 ? MoneyDirection.In : MoneyDirection.Out, Math.Abs(delta), AccountTransactionCategory.OpeningBalance,
            new LedgerSource("FinancialAccount", id, null, description), clock.UtcNow, ct);
        await db.Database.ExecuteSqlAsync($"UPDATE FinancialAccounts SET OpeningBalance = {request.OpeningBalance} WHERE Id = {id}", ct);

        audit.Log("Account.SetOpeningBalance", "FinancialAccount", id.ToString(), new { account.OpeningBalance }, new { request.OpeningBalance }, request.Notes);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<AccountStatementDto> StatementAsync(int id, DateOnly? from, DateOnly? to, PagedRequest paging, CancellationToken ct = default)
    {
        var account = await db.FinancialAccounts.AsNoTracking().Where(a => a.Id == id).Select(a => new { a.Id, a.Name }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("الحساب غير موجود");
        var end = to ?? clock.LocalToday;
        var (startUtc, endUtc) = clock.LocalDaysToUtcRange(from ?? new DateOnly(2000, 1, 1), end);
        var all = db.AccountTransactions.AsNoTracking().Where(t => t.FinancialAccountId == id);

        var opening = from is null ? 0 : await all.Where(t => t.Date < startUtc)
            .SumAsync(t => t.Direction == MoneyDirection.In ? t.Amount : -t.Amount, ct);
        var period = all.Where(t => t.Date >= startUtc && t.Date < endUtc);
        var totalIn = await period.Where(t => t.Direction == MoneyDirection.In).SumAsync(t => (decimal?)t.Amount, ct) ?? 0;
        var totalOut = await period.Where(t => t.Direction == MoneyDirection.Out).SumAsync(t => (decimal?)t.Amount, ct) ?? 0;

        var users = db.Users;
        var page = await period.OrderByDescending(t => t.Id)
            .Select(t => new { t.Id, t.Date, t.Direction, t.Amount, t.BalanceAfter, t.Category, t.SourceType, t.SourceId, t.SourceNumber, t.Description,
                UserName = users.Where(u => u.Id == t.UserId).Select(u => u.FullName).FirstOrDefault() })
            .ToPagedAsync(paging, ct);

        var movements = new PagedResult<AccountMovementDto>(
            page.Items.Select(t => new AccountMovementDto(t.Id, t.Date, t.Direction, t.Amount, t.BalanceAfter, t.Category,
                FinanceNames.Category(t.Category), t.SourceType, t.SourceId, t.SourceNumber, t.Description, t.UserName)).ToList(),
            page.Page, page.PageSize, page.TotalCount);

        return new AccountStatementDto(account.Id, account.Name, from, end, opening, totalIn, totalOut, opening + totalIn - totalOut, movements);
    }

    public async Task<PagedResult<AccountTransferDto>> TransfersAsync(PagedRequest request, CancellationToken ct = default)
    {
        var users = db.Users;
        return await db.AccountTransfers.AsNoTracking().OrderByDescending(t => t.Id)
            .Select(t => new AccountTransferDto(t.Id, t.Number, t.Date, t.FromAccountId, t.FromAccount.Name, t.ToAccountId, t.ToAccount.Name,
                t.Amount, t.Notes, users.Where(u => u.Id == t.CreatedById).Select(u => u.FullName).FirstOrDefault()))
            .ToPagedAsync(request, ct);
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
    }

    private async Task EnsureUniqueNameAsync(FinancialAccount account, CancellationToken ct)
    {
        if (await db.FinancialAccounts.AnyAsync(a => a.Id != account.Id && a.Name == account.Name, ct))
            throw new ValidationFailedException("name", "يوجد حساب بنفس الاسم");
    }

    private static AccountDto ToDto(FinancialAccount a, decimal pending, bool showNumbers) =>
        new(a.Id, a.Name, a.Type, a.ProviderName, a.HolderName, showNumbers ? a.AccountNumber : Mask(a.AccountNumber),
            a.OpeningBalance, a.CurrentBalance, a.IsActive, pending);
}
