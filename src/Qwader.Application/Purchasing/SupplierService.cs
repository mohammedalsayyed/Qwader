using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Qwader.Application.Abstractions;
using Qwader.Application.Common;
using Qwader.Application.Ledgers;
using Qwader.Domain.Administration;
using Qwader.Domain.Purchasing;

namespace Qwader.Application.Purchasing;

public sealed record SupplierDto(
    int Id, string Code, string Name, string? Phone, string? Address, string? Notes, decimal Balance, bool IsActive,
    decimal TotalPurchases, decimal TotalPayments, DateTime CreatedAt);

public sealed record SupplierListItemDto(int Id, string Code, string Name, string? Phone, decimal Balance, bool IsActive);

public sealed record SaveSupplierRequest(string Name, string? Phone, string? Address, string? Notes, bool IsActive = true, decimal OpeningBalance = 0);

public sealed class SupplierListRequest : PagedRequest
{
    public bool? IsActive { get; set; }
    public bool? WithBalance { get; set; }
}

public sealed record StatementLineDto(DateTime Date, string Type, string? Reference, string SourceType, int? SourceId, string? Description, decimal Debit, decimal Credit, decimal Balance);

public sealed record StatementDto(
    int PartyId, string PartyName, DateOnly? From, DateOnly To, decimal OpeningBalance,
    decimal TotalDebit, decimal TotalCredit, decimal ClosingBalance, IReadOnlyList<StatementLineDto> Lines);

public interface ISupplierService
{
    Task<PagedResult<SupplierListItemDto>> ListAsync(SupplierListRequest request, CancellationToken ct = default);
    Task<SupplierDto> GetAsync(int id, CancellationToken ct = default);
    Task<SupplierDto> CreateAsync(SaveSupplierRequest request, CancellationToken ct = default);
    Task<SupplierDto> UpdateAsync(int id, SaveSupplierRequest request, CancellationToken ct = default);
    Task<StatementDto> StatementAsync(int id, DateOnly? from, DateOnly? to, CancellationToken ct = default);
}

public sealed class SaveSupplierRequestValidator : AbstractValidator<SaveSupplierRequest>
{
    public SaveSupplierRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("اسم المورد مطلوب").MaximumLength(150);
        RuleFor(x => x.Phone).MaximumLength(20).WithMessage("رقم الجوال طويل جدًا");
        RuleFor(x => x.Address).MaximumLength(200);
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.OpeningBalance).GreaterThanOrEqualTo(0).WithMessage("الرصيد الافتتاحي لا يمكن أن يكون سالبًا");
    }
}

public sealed class SupplierService(
    IAppDbContext db,
    IDocumentNumberGenerator numbers,
    ISupplierLedger ledger,
    IClock clock,
    IAuditService audit) : ISupplierService
{
    public static readonly IReadOnlyDictionary<SupplierEntryType, string> EntryNames = new Dictionary<SupplierEntryType, string>
    {
        [SupplierEntryType.Opening] = "رصيد افتتاحي",
        [SupplierEntryType.Purchase] = "فاتورة شراء",
        [SupplierEntryType.Payment] = "دفعة للمورد",
        [SupplierEntryType.Return] = "مردود مشتريات",
        [SupplierEntryType.Reversal] = "قيد عكسي",
        [SupplierEntryType.Refund] = "استرداد من المورد",
    };

    public async Task<PagedResult<SupplierListItemDto>> ListAsync(SupplierListRequest request, CancellationToken ct = default)
    {
        var query = db.Suppliers.AsNoTracking();
        if (request.IsActive is { } active) query = query.Where(s => s.IsActive == active);
        if (request.WithBalance == true) query = query.Where(s => s.Balance != 0);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var raw = request.Search.Trim();
            foreach (var term in ArabicText.Normalize(raw).Split(' ', StringSplitOptions.RemoveEmptyEntries))
                query = query.Where(s => s.NormalizedName.Contains(term) || s.Phone!.Contains(term) || s.Code == raw);
        }

        return await query.OrderByDescending(s => s.IsActive).ThenBy(s => s.Name)
            .Select(s => new SupplierListItemDto(s.Id, s.Code, s.Name, s.Phone, s.Balance, s.IsActive))
            .ToPagedAsync(request, ct);
    }

    public async Task<SupplierDto> GetAsync(int id, CancellationToken ct = default) =>
        await db.Suppliers.AsNoTracking().Where(s => s.Id == id)
            .Select(s => new SupplierDto(s.Id, s.Code, s.Name, s.Phone, s.Address, s.Notes, s.Balance, s.IsActive,
                // Net purchases: posted invoices minus returns.
                (db.PurchaseInvoices.Where(p => p.SupplierId == s.Id && p.Status == PurchaseStatus.Posted).Sum(p => (decimal?)p.Total) ?? 0)
                    - (db.PurchaseReturns.Where(r => r.SupplierId == s.Id).Sum(r => (decimal?)r.Total) ?? 0),
                // Payments made, minus payments reversed by voiding an invoice (the only source of reversal credits).
                (db.SupplierLedgerEntries.Where(e => e.SupplierId == s.Id && e.Type == SupplierEntryType.Payment).Sum(e => (decimal?)e.Debit) ?? 0)
                    - (db.SupplierLedgerEntries.Where(e => e.SupplierId == s.Id && e.Type == SupplierEntryType.Reversal).Sum(e => (decimal?)e.Credit) ?? 0),
                s.CreatedAt))
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("المورد غير موجود");

    public async Task<SupplierDto> CreateAsync(SaveSupplierRequest request, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var supplier = new Supplier { Code = await numbers.NextAsync(DocumentType.Supplier, ct) };
        Apply(supplier, request);
        await EnsureUniqueNameAsync(supplier, ct);
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync(ct);

        if (request.OpeningBalance > 0)
            await ledger.PostAsync(supplier.Id, SupplierEntryType.Opening, 0, request.OpeningBalance,
                new LedgerSource("Supplier", supplier.Id, supplier.Code, "رصيد افتتاحي مستحق للمورد"), clock.UtcNow, ct);

        audit.Log("Supplier.Create", "Supplier", supplier.Id.ToString(), null, request);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(supplier.Id, ct);
    }

    public async Task<SupplierDto> UpdateAsync(int id, SaveSupplierRequest request, CancellationToken ct = default)
    {
        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("المورد غير موجود");
        var before = new { supplier.Name, supplier.Phone, supplier.Address, supplier.IsActive };
        Apply(supplier, request);
        await EnsureUniqueNameAsync(supplier, ct);
        audit.Log("Supplier.Update", "Supplier", id.ToString(), before, new { supplier.Name, supplier.Phone, supplier.Address, supplier.IsActive });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<StatementDto> StatementAsync(int id, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var supplier = await db.Suppliers.AsNoTracking().Where(s => s.Id == id).Select(s => new { s.Id, s.Name }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("المورد غير موجود");
        var end = to ?? clock.LocalToday;
        var (startUtc, endUtc) = clock.LocalDaysToUtcRange(from ?? new DateOnly(2000, 1, 1), end);

        var entries = db.SupplierLedgerEntries.AsNoTracking().Where(e => e.SupplierId == id);
        var opening = from is null ? 0 : await entries.Where(e => e.Date < startUtc).SumAsync(e => e.Credit - e.Debit, ct);
        var rows = await entries.Where(e => e.Date >= startUtc && e.Date < endUtc).OrderBy(e => e.Date).ThenBy(e => e.Id)
            .Select(e => new { e.Date, e.Type, e.SourceNumber, e.SourceType, e.SourceId, e.Description, e.Debit, e.Credit })
            .ToListAsync(ct);

        var running = opening;
        var lines = rows.Select(r =>
        {
            running += r.Credit - r.Debit;
            return new StatementLineDto(r.Date, EntryNames[r.Type], r.SourceNumber, r.SourceType, r.SourceId, r.Description, r.Debit, r.Credit, running);
        }).ToList();

        return new StatementDto(supplier.Id, supplier.Name, from, end, opening, rows.Sum(r => r.Debit), rows.Sum(r => r.Credit), running, lines);
    }

    private static void Apply(Supplier supplier, SaveSupplierRequest r)
    {
        supplier.Name = r.Name.Trim();
        supplier.NormalizedName = ArabicText.Normalize(r.Name);
        supplier.Phone = string.IsNullOrWhiteSpace(r.Phone) ? null : r.Phone.Trim();
        supplier.Address = string.IsNullOrWhiteSpace(r.Address) ? null : r.Address.Trim();
        supplier.Notes = string.IsNullOrWhiteSpace(r.Notes) ? null : r.Notes.Trim();
        supplier.IsActive = r.IsActive;
    }

    private async Task EnsureUniqueNameAsync(Supplier supplier, CancellationToken ct)
    {
        if (await db.Suppliers.AnyAsync(s => s.Id != supplier.Id && s.NormalizedName == supplier.NormalizedName, ct))
            throw new ValidationFailedException("name", "يوجد مورد بنفس الاسم");
    }
}
