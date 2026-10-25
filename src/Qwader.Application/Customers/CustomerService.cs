using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Qwader.Application.Abstractions;
using Qwader.Application.Common;
using Qwader.Application.Ledgers;
using Qwader.Application.Purchasing;
using Qwader.Domain.Administration;
using Qwader.Domain.Sales;

namespace Qwader.Application.Customers;

public sealed record CustomerListItemDto(int Id, string Code, string Name, string? Phone, decimal Balance, bool IsActive, bool IsWalkIn);

public sealed record CustomerDto(
    int Id, string Code, string Name, string? Phone, string? Address, string? Notes, decimal Balance, bool IsActive, bool IsWalkIn,
    decimal TotalSales, decimal TotalReturns, int InvoiceCount, DateTime? LastPurchaseAt, DateTime CreatedAt);

public sealed record SaveCustomerRequest(string Name, string? Phone, string? Address, string? Notes, bool IsActive = true, decimal OpeningBalance = 0);

public sealed class CustomerListRequest : PagedRequest
{
    public bool? WithBalance { get; set; }
    public bool? IsActive { get; set; }
}

public interface ICustomerService
{
    Task<PagedResult<CustomerListItemDto>> ListAsync(CustomerListRequest request, CancellationToken ct = default);
    Task<CustomerDto> GetAsync(int id, CancellationToken ct = default);
    Task<CustomerDto> CreateAsync(SaveCustomerRequest request, CancellationToken ct = default);
    Task<CustomerDto> UpdateAsync(int id, SaveCustomerRequest request, CancellationToken ct = default);
    Task<StatementDto> StatementAsync(int id, DateOnly? from, DateOnly? to, CancellationToken ct = default);

    /// <summary>Used by the till: an existing customer with this phone, or a new one. Must run inside the caller's transaction.</summary>
    Task<Customer> FindOrCreateByPhoneAsync(string name, string phone, CancellationToken ct = default);
}

public static class PhoneNumbers
{
    /// <summary>Digits only (Arabic-Indic converted), so «059 912-3456» and «٠٥٩٩١٢٣٤٥٦» match.</summary>
    public static string? Normalize(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var digits = new string(ArabicText.Normalize(phone).Where(c => char.IsAsciiDigit(c) || c == '+').ToArray());
        return digits.Length == 0 ? null : digits;
    }
}

public sealed class SaveCustomerRequestValidator : AbstractValidator<SaveCustomerRequest>
{
    public SaveCustomerRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("اسم العميل مطلوب").MaximumLength(150);
        RuleFor(x => x.Phone).MaximumLength(20).WithMessage("رقم الجوال طويل جدًا")
            .Must(p => p is null || PhoneNumbers.Normalize(p)?.Length is >= 7).WithMessage("رقم الجوال غير صحيح");
        RuleFor(x => x.Address).MaximumLength(200);
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.OpeningBalance).GreaterThanOrEqualTo(0).WithMessage("الرصيد الافتتاحي لا يمكن أن يكون سالبًا");
    }
}

public sealed class CustomerService(
    IAppDbContext db,
    IDocumentNumberGenerator numbers,
    ICustomerLedger ledger,
    IClock clock,
    IAuditService audit) : ICustomerService
{
    public static readonly IReadOnlyDictionary<CustomerEntryType, string> EntryNames = new Dictionary<CustomerEntryType, string>
    {
        [CustomerEntryType.Opening] = "رصيد افتتاحي",
        [CustomerEntryType.Sale] = "فاتورة بيع",
        [CustomerEntryType.Payment] = "دفعة من العميل",
        [CustomerEntryType.Return] = "مرتجع مبيعات",
        [CustomerEntryType.Refund] = "مبلغ مُعاد للعميل",
        [CustomerEntryType.Reversal] = "قيد عكسي",
    };

    public async Task<PagedResult<CustomerListItemDto>> ListAsync(CustomerListRequest request, CancellationToken ct = default)
    {
        var query = db.Customers.AsNoTracking();
        if (request.IsActive is { } active) query = query.Where(c => c.IsActive == active);
        if (request.WithBalance == true) query = query.Where(c => c.Balance != 0);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var raw = request.Search.Trim();
            var phone = PhoneNumbers.Normalize(raw);
            foreach (var term in ArabicText.Normalize(raw).Split(' ', StringSplitOptions.RemoveEmptyEntries))
                query = query.Where(c => c.NormalizedName.Contains(term) || c.Code == raw || (phone != null && c.Phone!.Contains(phone)));
        }

        return await query.OrderByDescending(c => c.IsWalkIn).ThenByDescending(c => c.Balance > 0).ThenBy(c => c.Name)
            .Select(c => new CustomerListItemDto(c.Id, c.Code, c.Name, c.Phone, c.Balance, c.IsActive, c.IsWalkIn))
            .ToPagedAsync(request, ct);
    }

    public async Task<CustomerDto> GetAsync(int id, CancellationToken ct = default) =>
        await db.Customers.AsNoTracking().Where(c => c.Id == id)
            .Select(c => new CustomerDto(c.Id, c.Code, c.Name, c.Phone, c.Address, c.Notes, c.Balance, c.IsActive, c.IsWalkIn,
                db.SaleInvoices.Where(s => s.CustomerId == c.Id && s.Status != SaleStatus.Voided).Sum(s => (decimal?)s.Total) ?? 0,
                db.SaleReturns.Where(r => r.CustomerId == c.Id).Sum(r => (decimal?)r.Total) ?? 0,
                db.SaleInvoices.Count(s => s.CustomerId == c.Id && s.Status != SaleStatus.Voided),
                db.SaleInvoices.Where(s => s.CustomerId == c.Id && s.Status != SaleStatus.Voided).Max(s => (DateTime?)s.Date),
                c.CreatedAt))
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("العميل غير موجود");

    public async Task<CustomerDto> CreateAsync(SaveCustomerRequest request, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var customer = new Customer { Code = await numbers.NextAsync(DocumentType.Customer, ct) };
        Apply(customer, request);
        await EnsureUniquePhoneAsync(customer, ct);
        db.Customers.Add(customer);
        await db.SaveChangesAsync(ct);

        if (request.OpeningBalance > 0)
            await ledger.PostAsync(customer.Id, CustomerEntryType.Opening, request.OpeningBalance, 0,
                new LedgerSource("Customer", customer.Id, customer.Code, "دين سابق قبل استخدام النظام"), clock.UtcNow, ct);

        audit.Log("Customer.Create", "Customer", customer.Id.ToString(), null, request);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(customer.Id, ct);
    }

    public async Task<CustomerDto> UpdateAsync(int id, SaveCustomerRequest request, CancellationToken ct = default)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("العميل غير موجود");
        if (customer.IsWalkIn) throw new BusinessRuleException("«زبون نقدي» حساب أساسي في النظام ولا يمكن تعديله");
        var before = new { customer.Name, customer.Phone, customer.Address, customer.IsActive };
        Apply(customer, request);
        await EnsureUniquePhoneAsync(customer, ct);
        audit.Log("Customer.Update", "Customer", id.ToString(), before, new { customer.Name, customer.Phone, customer.Address, customer.IsActive });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<Customer> FindOrCreateByPhoneAsync(string name, string phone, CancellationToken ct = default)
    {
        var normalized = PhoneNumbers.Normalize(phone) ?? throw new ValidationFailedException("customerPhone", "رقم الجوال غير صحيح");
        var existing = await db.Customers.FirstOrDefaultAsync(c => c.Phone == normalized, ct);
        if (existing is not null) return existing;

        var customer = new Customer { Code = await numbers.NextAsync(DocumentType.Customer, ct) };
        Apply(customer, new SaveCustomerRequest(name, normalized, null, null));
        db.Customers.Add(customer);
        await db.SaveChangesAsync(ct);
        return customer;
    }

    public async Task<StatementDto> StatementAsync(int id, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var customer = await db.Customers.AsNoTracking().Where(c => c.Id == id).Select(c => new { c.Id, c.Name }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("العميل غير موجود");
        var end = to ?? clock.LocalToday;
        var (startUtc, endUtc) = clock.LocalDaysToUtcRange(from ?? new DateOnly(2000, 1, 1), end);

        var entries = db.CustomerLedgerEntries.AsNoTracking().Where(e => e.CustomerId == id);
        var opening = from is null ? 0 : await entries.Where(e => e.Date < startUtc).SumAsync(e => e.Debit - e.Credit, ct);
        var rows = await entries.Where(e => e.Date >= startUtc && e.Date < endUtc).OrderBy(e => e.Date).ThenBy(e => e.Id)
            .Select(e => new { e.Date, e.Type, e.SourceNumber, e.SourceType, e.SourceId, e.Description, e.Debit, e.Credit })
            .ToListAsync(ct);

        var running = opening;
        var lines = rows.Select(r =>
        {
            running += r.Debit - r.Credit;
            // Statement lines use the shared shape: Debit/Credit columns keep their customer meaning.
            return new StatementLineDto(r.Date, EntryNames[r.Type], r.SourceNumber, r.SourceType, r.SourceId, r.Description, r.Debit, r.Credit, running);
        }).ToList();

        return new StatementDto(customer.Id, customer.Name, from, end, opening, rows.Sum(r => r.Debit), rows.Sum(r => r.Credit), running, lines);
    }

    private static void Apply(Customer customer, SaveCustomerRequest r)
    {
        customer.Name = r.Name.Trim();
        customer.NormalizedName = ArabicText.Normalize(r.Name);
        customer.Phone = PhoneNumbers.Normalize(r.Phone);
        customer.Address = string.IsNullOrWhiteSpace(r.Address) ? null : r.Address.Trim();
        customer.Notes = string.IsNullOrWhiteSpace(r.Notes) ? null : r.Notes.Trim();
        customer.IsActive = r.IsActive;
    }

    private async Task EnsureUniquePhoneAsync(Customer customer, CancellationToken ct)
    {
        if (customer.Phone is { } phone && await db.Customers.AnyAsync(c => c.Id != customer.Id && c.Phone == phone, ct))
            throw new ValidationFailedException("phone", "رقم الجوال مسجّل لعميل آخر");
    }
}
