using Microsoft.EntityFrameworkCore;
using Qwader.Application.Abstractions;
using Qwader.Application.Common;
using Qwader.Application.Ledgers;
using Qwader.Domain.Administration;
using Qwader.Domain.Files;
using Qwader.Domain.Finance;
using Qwader.Domain.Purchasing;
using Qwader.Domain.Sales;

namespace Qwader.Application.Finance;

public interface IVoucherService
{
    Task<PagedResult<VoucherDto>> ReceiptsAsync(VoucherListRequest request, CancellationToken ct = default);
    Task<VoucherDto> GetReceiptAsync(int id, CancellationToken ct = default);
    Task<VoucherDto> CreateReceiptAsync(CreateReceiptRequest request, CancellationToken ct = default);
    Task<VoucherDto> VoidReceiptAsync(int id, VoidDocumentRequest request, CancellationToken ct = default);

    Task<PagedResult<VoucherDto>> PaymentsAsync(VoucherListRequest request, CancellationToken ct = default);
    Task<VoucherDto> GetPaymentAsync(int id, CancellationToken ct = default);
    Task<VoucherDto> CreatePaymentAsync(CreatePaymentRequest request, CancellationToken ct = default);
    Task<VoucherDto> VoidPaymentAsync(int id, VoidDocumentRequest request, CancellationToken ct = default);

    Task<PagedResult<ExpenseDto>> ExpensesAsync(VoucherListRequest request, CancellationToken ct = default);
    Task<ExpenseDto> CreateExpenseAsync(CreateExpenseRequest request, CancellationToken ct = default);
    Task<ExpenseDto> VoidExpenseAsync(int id, VoidDocumentRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<ExpenseCategoryDto>> CategoriesAsync(CancellationToken ct = default);
    Task<ExpenseCategoryDto> SaveCategoryAsync(int? id, SaveExpenseCategoryRequest request, CancellationToken ct = default);

    /// <summary>
    /// Posts a receipt inside the caller's transaction (used when a transfer is confirmed). Applies all effects:
    /// money into the account and, for a customer, the reduction of their debt.
    /// </summary>
    Task<ReceiptVoucher> PostReceiptAsync(ReceiptVoucher voucher, CancellationToken ct = default);
}

public sealed class VoucherService(
    IAppDbContext db,
    IAccountLedger accounts,
    ICustomerLedger customers,
    ISupplierLedger suppliers,
    IDocumentNumberGenerator numbers,
    IIdempotencyService idempotency,
    ICurrentUser currentUser,
    IClock clock,
    IAuditService audit) : IVoucherService
{
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromMinutes(10);

    // ------------------------------------------------------------------ receipts

    public async Task<PagedResult<VoucherDto>> ReceiptsAsync(VoucherListRequest request, CancellationToken ct = default)
    {
        var query = db.ReceiptVouchers.AsNoTracking();
        if (request.IncludeVoided != true) query = query.Where(v => v.Status == VoucherStatus.Posted);
        if (request.AccountId is { } accountId) query = query.Where(v => v.FinancialAccountId == accountId);
        if (request.CustomerId is { } customerId) query = query.Where(v => v.CustomerId == customerId);
        if (request.MinAmount is { } min) query = query.Where(v => v.Amount >= min);
        if (request.MaxAmount is { } max) query = query.Where(v => v.Amount <= max);
        if (request.From is not null || request.To is not null)
        {
            var (start, end) = clock.LocalDaysToUtcRange(request.From ?? new DateOnly(2000, 1, 1), request.To ?? clock.LocalToday);
            query = query.Where(v => v.Date >= start && v.Date < end);
        }
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var raw = request.Search.Trim();
            var term = ArabicText.Normalize(raw);
            query = query.Where(v => v.Number.EndsWith(raw) || v.PayerName.Contains(raw) || (v.Customer != null && v.Customer.NormalizedName.Contains(term))
                || v.Reference == raw || v.PayerPhone!.Contains(raw));
        }
        return await ProjectReceipts(query.OrderByDescending(v => v.Id)).ToPagedAsync(request, ct);
    }

    public async Task<VoucherDto> GetReceiptAsync(int id, CancellationToken ct = default) =>
        await ProjectReceipts(db.ReceiptVouchers.AsNoTracking().Where(v => v.Id == id)).FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException("السند غير موجود");

    public async Task<VoucherDto> CreateReceiptAsync(CreateReceiptRequest request, CancellationToken ct = default)
    {
        const string operation = "Voucher.Receipt";
        if (await idempotency.FindAsync(request.IdempotencyKey, operation, ct) is { } done) return await GetReceiptAsync(int.Parse(done.ResultId!), ct);
        await EnsureAttachmentAsync(request.AttachmentFileId, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        Customer? customer = null;
        if (request.CustomerId is { } customerId)
        {
            customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == customerId, ct) ?? throw new ValidationFailedException("customerId", "العميل غير موجود");
            if (customer.IsWalkIn) throw new ValidationFailedException("customerId", "اختر عميلًا باسمه");
            if (request.Purpose == ReceiptPurpose.DebtCollection && request.Amount > customer.Balance + 0.009m)
                throw new BusinessRuleException(customer.Balance <= 0
                    ? $"لا يوجد دين على «{customer.Name}»"
                    : $"المبلغ أكبر من دين «{customer.Name}» ({customer.Balance:0.00} ₪)");
        }

        var payerName = customer?.Name ?? request.PayerName!.Trim();
        if (!request.ConfirmDuplicate)
        {
            var since = clock.UtcNow - DuplicateWindow;
            var duplicate = await db.ReceiptVouchers.AnyAsync(v => v.Status == VoucherStatus.Posted && v.Date >= since && v.Amount == request.Amount
                && v.FinancialAccountId == request.FinancialAccountId && (customer != null ? v.CustomerId == customer.Id : v.PayerName == payerName), ct);
            if (duplicate)
                throw new ConflictException("سُجّل قبل دقائق سند قبض بنفس المبلغ لنفس الدافع ونفس الحساب. إذا كانت دفعة جديدة فعلًا، أكّد التسجيل.");
        }

        var voucher = await PostReceiptAsync(new ReceiptVoucher
        {
            Date = clock.UtcNow,
            Purpose = request.Purpose,
            CustomerId = customer?.Id,
            PayerName = payerName,
            PayerPhone = customer?.Phone ?? Clean(request.PayerPhone),
            Amount = request.Amount,
            FinancialAccountId = request.FinancialAccountId,
            Reference = Clean(request.Reference),
            Notes = Clean(request.Notes),
            AttachmentFileId = request.AttachmentFileId,
        }, ct);

        idempotency.Record(request.IdempotencyKey, operation, "ReceiptVoucher", voucher.Id.ToString());
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetReceiptAsync(voucher.Id, ct);
    }

    public async Task<ReceiptVoucher> PostReceiptAsync(ReceiptVoucher voucher, CancellationToken ct = default)
    {
        voucher.Number = await numbers.NextAsync(DocumentType.ReceiptVoucher, ct);
        db.ReceiptVouchers.Add(voucher);
        await db.SaveChangesAsync(ct);

        var source = new LedgerSource("ReceiptVoucher", voucher.Id, voucher.Number, $"سند قبض — {voucher.PayerName}");
        await accounts.PostAsync(voucher.FinancialAccountId, MoneyDirection.In, voucher.Amount,
            voucher.Purpose == ReceiptPurpose.DebtCollection ? AccountTransactionCategory.DebtCollection : AccountTransactionCategory.OtherIncome,
            source, voucher.Date, ct);
        if (voucher.CustomerId is { } customerId && voucher.Purpose == ReceiptPurpose.DebtCollection)
            await customers.PostAsync(customerId, CustomerEntryType.Payment, 0, voucher.Amount, source, voucher.Date, ct);
        await AllocateAsync(voucher, ct);

        audit.Log("Voucher.Receipt", "ReceiptVoucher", voucher.Id.ToString(), null, new { voucher.Number, voucher.Amount, voucher.PayerName, voucher.Purpose });
        return voucher;
    }

    /// <summary>
    /// A collection pays the customer's debts so that each invoice shows what is still owed on it:
    /// first the invoice it was for (a confirmed till transfer), then the opening balance, then the oldest open invoices.
    /// Whatever is left is the customer's credit («له»). Runs after the customer ledger update, whose row lock
    /// serializes collections for the same customer.
    /// </summary>
    private async Task AllocateAsync(ReceiptVoucher voucher, CancellationToken ct)
    {
        if (voucher.CustomerId is not { } customerId || voucher.Purpose != ReceiptPurpose.DebtCollection) return;

        var open = await db.SaleInvoices.Where(s => s.CustomerId == customerId && s.Status != SaleStatus.Voided)
            .Select(s => new
            {
                s.Id, s.Date,
                Open = s.Total - s.PaidAmount - s.CollectedAmount
                    - (db.SaleReturns.Where(r => r.SaleInvoiceId == s.Id && r.Settlement == ReturnSettlement.ReduceBalance).Sum(r => (decimal?)r.Total) ?? 0),
            })
            .Where(x => x.Open > 0.004m).OrderBy(x => x.Date).ThenBy(x => x.Id).ToListAsync(ct);
        var openingDebt = await db.CustomerLedgerEntries.Where(e => e.CustomerId == customerId && e.Type == CustomerEntryType.Opening)
            .SumAsync(e => (decimal?)(e.Debit - e.Credit), ct) ?? 0;
        var openingPaid = await db.ReceiptAllocations.Where(a => a.CustomerId == customerId && a.SaleInvoiceId == null)
            .SumAsync(a => (decimal?)a.Amount, ct) ?? 0;

        var left = voucher.Amount;
        var allocations = new List<ReceiptAllocation>();
        void Take(int? invoiceId, decimal owed)
        {
            var amount = Math.Min(left, owed);
            if (amount <= 0.004m) return;
            allocations.Add(new ReceiptAllocation { ReceiptVoucherId = voucher.Id, CustomerId = customerId, SaleInvoiceId = invoiceId, Amount = amount });
            left -= amount;
        }

        if (open.FirstOrDefault(i => i.Id == voucher.SaleInvoiceId) is { } preferred) Take(preferred.Id, preferred.Open);
        Take(null, openingDebt - openingPaid);
        foreach (var invoice in open.Where(i => i.Id != voucher.SaleInvoiceId)) Take(invoice.Id, invoice.Open);

        db.ReceiptAllocations.AddRange(allocations);
        foreach (var a in allocations.Where(a => a.SaleInvoiceId is not null))
            await db.SaleInvoices.Where(s => s.Id == a.SaleInvoiceId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.CollectedAmount, x => x.CollectedAmount + a.Amount), ct);
    }

    /// <summary>A voided collection gives back what it paid: the invoices are owed again.</summary>
    private async Task ReleaseAsync(ReceiptVoucher voucher, CancellationToken ct)
    {
        var allocations = await db.ReceiptAllocations.Where(a => a.ReceiptVoucherId == voucher.Id).ToListAsync(ct);
        foreach (var a in allocations.Where(a => a.SaleInvoiceId is not null))
            await db.SaleInvoices.Where(s => s.Id == a.SaleInvoiceId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.CollectedAmount, x => x.CollectedAmount - a.Amount), ct);
        db.ReceiptAllocations.RemoveRange(allocations);
    }

    public async Task<VoucherDto> VoidReceiptAsync(int id, VoidDocumentRequest request, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var voucher = await db.ReceiptVouchers.FirstOrDefaultAsync(v => v.Id == id, ct) ?? throw new NotFoundException("السند غير موجود");
        if (voucher.Status == VoucherStatus.Voided) throw new BusinessRuleException("السند ملغى من قبل");
        if (voucher.IncomingTransferId is not null) throw new BusinessRuleException("هذا السند ناتج عن حوالة مؤكدة؛ لا يُلغى من هنا");

        var now = clock.UtcNow;
        var source = new LedgerSource("ReceiptVoucher", voucher.Id, voucher.Number, $"إلغاء سند قبض: {request.Reason.Trim()}");
        await accounts.PostAsync(voucher.FinancialAccountId, MoneyDirection.Out, voucher.Amount, AccountTransactionCategory.Reversal, source, now, ct);
        if (voucher.CustomerId is { } customerId && voucher.Purpose == ReceiptPurpose.DebtCollection)
            await customers.PostAsync(customerId, CustomerEntryType.Reversal, voucher.Amount, 0, source, now, ct);
        await ReleaseAsync(voucher, ct);

        voucher.Status = VoucherStatus.Voided;
        voucher.VoidedAt = now;
        voucher.VoidedById = currentUser.UserId;
        voucher.VoidReason = request.Reason.Trim();
        audit.Log("Voucher.VoidReceipt", "ReceiptVoucher", id.ToString(), null, new { voucher.Number, voucher.Amount }, voucher.VoidReason);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetReceiptAsync(id, ct);
    }

    private IQueryable<VoucherDto> ProjectReceipts(IQueryable<ReceiptVoucher> query)
    {
        var users = db.Users;
        return query.Select(v => new VoucherDto(v.Id, v.Number, v.Date, v.PayerName, v.PayerPhone, v.CustomerId, null, v.Amount,
            v.FinancialAccountId, v.FinancialAccount.Name, v.Purpose.ToString(),
            v.Purpose == ReceiptPurpose.DebtCollection ? "تحصيل دين من عميل" : "إيراد آخر",
            v.Reference, v.Notes, v.AttachmentFileId, v.Status, users.Where(u => u.Id == v.CreatedById).Select(u => u.FullName).FirstOrDefault(),
            v.VoidedAt, v.VoidReason, v.IncomingTransferId, v.SaleInvoiceId, null));
    }

    // ------------------------------------------------------------------ payments

    public async Task<PagedResult<VoucherDto>> PaymentsAsync(VoucherListRequest request, CancellationToken ct = default)
    {
        var query = db.PaymentVouchers.AsNoTracking();
        if (request.IncludeVoided != true) query = query.Where(v => v.Status == VoucherStatus.Posted);
        if (request.AccountId is { } accountId) query = query.Where(v => v.FinancialAccountId == accountId);
        if (request.SupplierId is { } supplierId) query = query.Where(v => v.SupplierId == supplierId);
        if (request.MinAmount is { } min) query = query.Where(v => v.Amount >= min);
        if (request.MaxAmount is { } max) query = query.Where(v => v.Amount <= max);
        if (request.From is not null || request.To is not null)
        {
            var (start, end) = clock.LocalDaysToUtcRange(request.From ?? new DateOnly(2000, 1, 1), request.To ?? clock.LocalToday);
            query = query.Where(v => v.Date >= start && v.Date < end);
        }
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var raw = request.Search.Trim();
            query = query.Where(v => v.Number.EndsWith(raw) || v.BeneficiaryName.Contains(raw) || v.Reference == raw);
        }
        return await ProjectPayments(query.OrderByDescending(v => v.Id)).ToPagedAsync(request, ct);
    }

    public async Task<VoucherDto> GetPaymentAsync(int id, CancellationToken ct = default) =>
        await ProjectPayments(db.PaymentVouchers.AsNoTracking().Where(v => v.Id == id)).FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException("السند غير موجود");

    public async Task<VoucherDto> CreatePaymentAsync(CreatePaymentRequest request, CancellationToken ct = default)
    {
        const string operation = "Voucher.Payment";
        if (await idempotency.FindAsync(request.IdempotencyKey, operation, ct) is { } done) return await GetPaymentAsync(int.Parse(done.ResultId!), ct);
        await EnsureAttachmentAsync(request.AttachmentFileId, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        Supplier? supplier = null;
        if (request.SupplierId is { } supplierId)
        {
            supplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == supplierId, ct) ?? throw new ValidationFailedException("supplierId", "المورد غير موجود");
            if (request.Purpose == PaymentPurpose.SupplierPayment && request.Amount > supplier.Balance + 0.009m)
                throw new BusinessRuleException(supplier.Balance <= 0
                    ? $"لا يوجد مبلغ مستحق للمورد «{supplier.Name}»"
                    : $"المبلغ أكبر من المستحق للمورد «{supplier.Name}» ({supplier.Balance:0.00} ₪)");
        }

        var beneficiary = supplier?.Name ?? request.BeneficiaryName!.Trim();
        if (!request.ConfirmDuplicate)
        {
            var since = clock.UtcNow - DuplicateWindow;
            if (await db.PaymentVouchers.AnyAsync(v => v.Status == VoucherStatus.Posted && v.Date >= since && v.Amount == request.Amount
                    && v.FinancialAccountId == request.FinancialAccountId && v.BeneficiaryName == beneficiary, ct))
                throw new ConflictException("سُجّل قبل دقائق سند صرف بنفس المبلغ لنفس المستفيد ونفس الحساب. إذا كانت دفعة جديدة فعلًا، أكّد التسجيل.");
        }

        var voucher = await PostPaymentAsync(new PaymentVoucher
        {
            Date = clock.UtcNow,
            Purpose = request.Purpose,
            SupplierId = supplier?.Id,
            BeneficiaryName = beneficiary,
            Amount = request.Amount,
            FinancialAccountId = request.FinancialAccountId,
            Reference = Clean(request.Reference),
            Notes = Clean(request.Notes),
            AttachmentFileId = request.AttachmentFileId,
        }, ct);

        idempotency.Record(request.IdempotencyKey, operation, "PaymentVoucher", voucher.Id.ToString());
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetPaymentAsync(voucher.Id, ct);
    }

    private async Task<PaymentVoucher> PostPaymentAsync(PaymentVoucher voucher, CancellationToken ct)
    {
        voucher.Number = await numbers.NextAsync(DocumentType.PaymentVoucher, ct);
        db.PaymentVouchers.Add(voucher);
        await db.SaveChangesAsync(ct);

        var source = new LedgerSource("PaymentVoucher", voucher.Id, voucher.Number, $"سند صرف — {voucher.BeneficiaryName}");
        var category = voucher.Purpose switch
        {
            PaymentPurpose.SupplierPayment => AccountTransactionCategory.SupplierPayment,
            PaymentPurpose.Expense => AccountTransactionCategory.Expense,
            _ => AccountTransactionCategory.OtherPayment,
        };
        await accounts.PostAsync(voucher.FinancialAccountId, MoneyDirection.Out, voucher.Amount, category, source, voucher.Date, ct, requireFunds: true);
        if (voucher.SupplierId is { } supplierId && voucher.Purpose == PaymentPurpose.SupplierPayment)
            await suppliers.PostAsync(supplierId, SupplierEntryType.Payment, voucher.Amount, 0, source, voucher.Date, ct);

        audit.Log("Voucher.Payment", "PaymentVoucher", voucher.Id.ToString(), null, new { voucher.Number, voucher.Amount, voucher.BeneficiaryName, voucher.Purpose });
        return voucher;
    }

    public async Task<VoucherDto> VoidPaymentAsync(int id, VoidDocumentRequest request, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var voucher = await db.PaymentVouchers.FirstOrDefaultAsync(v => v.Id == id, ct) ?? throw new NotFoundException("السند غير موجود");
        if (voucher.Purpose == PaymentPurpose.Expense) throw new BusinessRuleException("هذا السند لمصروف؛ ألغِ المصروف من شاشة المصروفات");
        await VoidPaymentCoreAsync(voucher, request.Reason.Trim(), ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetPaymentAsync(id, ct);
    }

    private async Task VoidPaymentCoreAsync(PaymentVoucher voucher, string reason, CancellationToken ct)
    {
        if (voucher.Status == VoucherStatus.Voided) throw new BusinessRuleException("السند ملغى من قبل");
        var now = clock.UtcNow;
        var source = new LedgerSource("PaymentVoucher", voucher.Id, voucher.Number, $"إلغاء سند صرف: {reason}");
        await accounts.PostAsync(voucher.FinancialAccountId, MoneyDirection.In, voucher.Amount, AccountTransactionCategory.Reversal, source, now, ct);
        if (voucher.SupplierId is { } supplierId && voucher.Purpose == PaymentPurpose.SupplierPayment)
            await suppliers.PostAsync(supplierId, SupplierEntryType.Reversal, 0, voucher.Amount, source, now, ct);

        voucher.Status = VoucherStatus.Voided;
        voucher.VoidedAt = now;
        voucher.VoidedById = currentUser.UserId;
        voucher.VoidReason = reason;
        audit.Log("Voucher.VoidPayment", "PaymentVoucher", voucher.Id.ToString(), null, new { voucher.Number, voucher.Amount }, reason);
    }

    private IQueryable<VoucherDto> ProjectPayments(IQueryable<PaymentVoucher> query)
    {
        var users = db.Users;
        var expenses = db.Expenses;
        return query.Select(v => new VoucherDto(v.Id, v.Number, v.Date, v.BeneficiaryName, null, null, v.SupplierId, v.Amount,
            v.FinancialAccountId, v.FinancialAccount.Name, v.Purpose.ToString(),
            v.Purpose == PaymentPurpose.SupplierPayment ? "دفعة لمورد" : v.Purpose == PaymentPurpose.Expense ? "مصروف" : "صرف آخر",
            v.Reference, v.Notes, v.AttachmentFileId, v.Status, users.Where(u => u.Id == v.CreatedById).Select(u => u.FullName).FirstOrDefault(),
            v.VoidedAt, v.VoidReason, null, null,
            expenses.Where(e => e.PaymentVoucherId == v.Id).Select(e => e.Category.Name).FirstOrDefault()));
    }

    // ------------------------------------------------------------------ expenses

    public async Task<PagedResult<ExpenseDto>> ExpensesAsync(VoucherListRequest request, CancellationToken ct = default)
    {
        var query = db.Expenses.AsNoTracking();
        if (request.IncludeVoided != true) query = query.Where(e => e.Status == VoucherStatus.Posted);
        if (request.CategoryId is { } categoryId) query = query.Where(e => e.ExpenseCategoryId == categoryId);
        if (request.AccountId is { } accountId) query = query.Where(e => e.FinancialAccountId == accountId);
        if (request.From is not null || request.To is not null)
        {
            var (start, end) = clock.LocalDaysToUtcRange(request.From ?? new DateOnly(2000, 1, 1), request.To ?? clock.LocalToday);
            query = query.Where(e => e.Date >= start && e.Date < end);
        }
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var raw = request.Search.Trim();
            query = query.Where(e => e.Number.EndsWith(raw) || e.Description.Contains(raw));
        }
        return await ProjectExpenses(query.OrderByDescending(e => e.Date).ThenByDescending(e => e.Id)).ToPagedAsync(request, ct);
    }

    public async Task<ExpenseDto> CreateExpenseAsync(CreateExpenseRequest request, CancellationToken ct = default)
    {
        const string operation = "Expense.Create";
        if (await idempotency.FindAsync(request.IdempotencyKey, operation, ct) is { } done)
            return await ProjectExpenses(db.Expenses.AsNoTracking().Where(e => e.Id == int.Parse(done.ResultId!))).FirstAsync(ct);
        await EnsureAttachmentAsync(request.AttachmentFileId, ct);
        var category = await db.ExpenseCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.ExpenseCategoryId, ct)
            ?? throw new ValidationFailedException("expenseCategoryId", "نوع المصروف غير موجود");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var date = DocumentDates.ToUtc(request.Date, clock);
        var description = request.Description.Trim();

        // The expense is paid through its own voucher: cash flow reads the voucher, profit reads the expense — never both.
        var voucher = await PostPaymentAsync(new PaymentVoucher
        {
            Date = date,
            Purpose = PaymentPurpose.Expense,
            BeneficiaryName = category.Name,
            Amount = request.Amount,
            FinancialAccountId = request.FinancialAccountId,
            Notes = description,
            AttachmentFileId = request.AttachmentFileId,
        }, ct);

        var expense = new Expense
        {
            Number = await numbers.NextAsync(DocumentType.Expense, ct),
            Date = date,
            ExpenseCategoryId = category.Id,
            Description = description,
            Amount = request.Amount,
            FinancialAccountId = request.FinancialAccountId,
            AttachmentFileId = request.AttachmentFileId,
            PaymentVoucherId = voucher.Id,
        };
        db.Expenses.Add(expense);
        await db.SaveChangesAsync(ct);

        idempotency.Record(request.IdempotencyKey, operation, "Expense", expense.Id.ToString());
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await ProjectExpenses(db.Expenses.AsNoTracking().Where(e => e.Id == expense.Id)).FirstAsync(ct);
    }

    public async Task<ExpenseDto> VoidExpenseAsync(int id, VoidDocumentRequest request, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var expense = await db.Expenses.Include(e => e.PaymentVoucher).FirstOrDefaultAsync(e => e.Id == id, ct) ?? throw new NotFoundException("المصروف غير موجود");
        if (expense.Status == VoucherStatus.Voided) throw new BusinessRuleException("المصروف ملغى من قبل");
        await VoidPaymentCoreAsync(expense.PaymentVoucher, request.Reason.Trim(), ct);
        expense.Status = VoucherStatus.Voided;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await ProjectExpenses(db.Expenses.AsNoTracking().Where(e => e.Id == id)).FirstAsync(ct);
    }

    private IQueryable<ExpenseDto> ProjectExpenses(IQueryable<Expense> query)
    {
        var users = db.Users;
        return query.Select(e => new ExpenseDto(e.Id, e.Number, e.Date, e.ExpenseCategoryId, e.Category.Name, e.Description, e.Amount,
            e.FinancialAccountId, e.FinancialAccount.Name, e.AttachmentFileId, e.PaymentVoucherId, e.PaymentVoucher.Number, e.Status,
            users.Where(u => u.Id == e.CreatedById).Select(u => u.FullName).FirstOrDefault()));
    }

    public async Task<IReadOnlyList<ExpenseCategoryDto>> CategoriesAsync(CancellationToken ct = default) =>
        await db.ExpenseCategories.AsNoTracking().OrderByDescending(c => c.IsActive).ThenBy(c => c.Id)
            .Select(c => new ExpenseCategoryDto(c.Id, c.Name, c.IsActive, db.Expenses.Count(e => e.ExpenseCategoryId == c.Id && e.Status == VoucherStatus.Posted)))
            .ToListAsync(ct);

    public async Task<ExpenseCategoryDto> SaveCategoryAsync(int? id, SaveExpenseCategoryRequest request, CancellationToken ct = default)
    {
        var name = request.Name.Trim();
        if (await db.ExpenseCategories.AnyAsync(c => c.Id != id && c.Name == name, ct)) throw new ValidationFailedException("name", "يوجد نوع بنفس الاسم");
        ExpenseCategory category;
        if (id is { } existingId)
            category = await db.ExpenseCategories.FirstOrDefaultAsync(c => c.Id == existingId, ct) ?? throw new NotFoundException("النوع غير موجود");
        else
        {
            category = new ExpenseCategory();
            db.ExpenseCategories.Add(category);
        }
        category.Name = name;
        category.IsActive = request.IsActive;
        audit.Log(id is null ? "ExpenseCategory.Create" : "ExpenseCategory.Update", "ExpenseCategory", id?.ToString(), null, request);
        await db.SaveChangesAsync(ct);
        return (await CategoriesAsync(ct)).First(c => c.Id == category.Id);
    }

    // ------------------------------------------------------------------ helpers

    private async Task EnsureAttachmentAsync(Guid? fileId, CancellationToken ct)
    {
        if (fileId is { } id && !await db.StoredFiles.AnyAsync(f => f.Id == id && f.Purpose != FilePurpose.ProductImage, ct))
            throw new ValidationFailedException("attachmentFileId", "المرفق غير موجود، أعد رفعه");
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
