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
    /// <summary>The customer's credit pays (part of) a new invoice of theirs.</summary>
    Task ApplyCreditAsync(int customerId, int saleInvoiceId, decimal open, decimal credit, CancellationToken ct = default);
    Task<VoucherDto> VoidReceiptAsync(int id, VoidDocumentRequest request, CancellationToken ct = default);

    Task<PagedResult<VoucherDto>> PaymentsAsync(VoucherListRequest request, CancellationToken ct = default);
    Task<VoucherDto> GetPaymentAsync(int id, CancellationToken ct = default);
    /// <summary>«مصاريف صاحب العمل»: the owner's withdrawals over a period (not voided).</summary>
    Task<OwnerExpensesTotalDto> OwnerExpensesTotalAsync(DateOnly? from, DateOnly? to, CancellationToken ct = default);
    Task<VoucherDto> CreatePaymentAsync(CreatePaymentRequest request, CancellationToken ct = default);
    Task<VoucherDto> VoidPaymentAsync(int id, VoidDocumentRequest request, CancellationToken ct = default);

    Task<PagedResult<ExpenseDto>> ExpensesAsync(VoucherListRequest request, CancellationToken ct = default);
    Task<ExpenseDto> CreateExpenseAsync(CreateExpenseRequest request, CancellationToken ct = default);
    Task<ExpenseDto> VoidExpenseAsync(int id, VoidDocumentRequest request, CancellationToken ct = default);
    /// <summary>Records a paid expense inside the caller's transaction (salaries and advances use it).</summary>
    Task<Expense> PostExpenseAsync(int categoryId, string description, decimal amount, int accountId, DateTime date, CancellationToken ct = default,
        Guid? attachmentFileId = null);
    /// <summary>Voids an expense and gives its money back to the account, inside the caller's transaction.</summary>
    Task VoidExpenseCoreAsync(int expenseId, string reason, CancellationToken ct = default);
    /// <summary>Pays an employee advance from an account inside the caller's transaction (not an expense).</summary>
    Task<PaymentVoucher> PostAdvanceAsync(string beneficiary, decimal amount, int accountId, DateTime date, string? notes, CancellationToken ct = default);
    /// <summary>Voids a payment voucher and gives its money back, inside the caller's transaction.</summary>
    Task VoidPaymentCoreAsync(int voucherId, string reason, CancellationToken ct = default);
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
        // A missing account is a clear message, not a database error.
        if (!await db.FinancialAccounts.AnyAsync(a => a.Id == request.FinancialAccountId && a.IsActive, ct))
            throw new ValidationFailedException("financialAccountId", "الحساب المالي غير موجود أو معطّل");
        Customer? customer = null;
        if (request.CustomerId is { } customerId)
        {
            // Locked first: two receipts at the same moment cannot both pass the «not more than the debt» check.
            await db.LockRowAsync("Customers", customerId, ct);
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

        // Every receipt is dated on the day the money came in (today by default) — not in the future, not before the system.
        var receiptDate = await DocumentDates.ToUtcAsync(request.Date, clock, db, ct);

        var voucher = await PostReceiptAsync(new ReceiptVoucher
        {
            Date = receiptDate,
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
        await accounts.PostAsync(voucher.FinancialAccountId, MoneyDirection.In, voucher.Amount, voucher.Purpose switch
            {
                ReceiptPurpose.DebtCollection => AccountTransactionCategory.DebtCollection,
                ReceiptPurpose.CustomerDeposit => AccountTransactionCategory.CustomerDeposit,
                ReceiptPurpose.CapitalContribution => AccountTransactionCategory.CapitalContribution,
                _ => AccountTransactionCategory.OtherIncome,
            }, source, voucher.Date, ct);
        // A collection or an advance goes to the customer's statement as a payment: their debt goes down, or their credit up.
        if (voucher.CustomerId is { } customerId && voucher.Purpose is ReceiptPurpose.DebtCollection or ReceiptPurpose.CustomerDeposit)
            await customers.PostAsync(customerId, CustomerEntryType.Payment, 0, voucher.Amount,
                voucher.Purpose == ReceiptPurpose.CustomerDeposit ? source with { Description = $"دفعة مقدمة — {voucher.PayerName}" } : source, voucher.Date, ct);
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
        // An advance from a customer who still owes pays those debts first, as a collection would; the rest is their credit.
        if (voucher.CustomerId is not { } customerId || voucher.Purpose is not (ReceiptPurpose.DebtCollection or ReceiptPurpose.CustomerDeposit)) return;

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

    /// <summary>
    /// A customer with credit («له») buys on credit: the part of their receipts not yet used (an advance, a collection
    /// beyond their debts, the money of a voided invoice) pays this invoice, so it does not show as owed while they are owed.
    /// Never more than the invoice leaves open nor than the credit they hold.
    /// </summary>
    public async Task ApplyCreditAsync(int customerId, int saleInvoiceId, decimal open, decimal credit, CancellationToken ct = default)
    {
        var left = Math.Min(open, credit);
        if (left <= 0.004m) return;
        var receipts = await db.ReceiptVouchers.Where(v => v.CustomerId == customerId && v.Status == VoucherStatus.Posted
                && (v.Purpose == ReceiptPurpose.DebtCollection || v.Purpose == ReceiptPurpose.CustomerDeposit))
            .Select(v => new { v.Id, v.Date, Free = v.Amount - (db.ReceiptAllocations.Where(a => a.ReceiptVoucherId == v.Id).Sum(a => (decimal?)a.Amount) ?? 0) })
            .Where(v => v.Free > 0.004m).OrderBy(v => v.Date).ThenBy(v => v.Id).ToListAsync(ct);
        var applied = 0m;
        foreach (var r in receipts)
        {
            var amount = Math.Min(left, r.Free);
            if (amount <= 0.004m) break;
            db.ReceiptAllocations.Add(new ReceiptAllocation { ReceiptVoucherId = r.Id, CustomerId = customerId, SaleInvoiceId = saleInvoiceId, Amount = amount });
            applied += amount;
            left -= amount;
        }
        if (applied > 0)
            await db.SaleInvoices.Where(s => s.Id == saleInvoiceId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.CollectedAmount, x => x.CollectedAmount + applied), ct);
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
        await db.LockRowAsync("ReceiptVouchers", id, ct);
        var voucher = await db.ReceiptVouchers.FirstOrDefaultAsync(v => v.Id == id, ct) ?? throw new NotFoundException("السند غير موجود");
        if (voucher.Status == VoucherStatus.Voided) throw new BusinessRuleException("السند ملغى من قبل");
        if (voucher.IncomingTransferId is not null) throw new BusinessRuleException("هذا السند ناتج عن حوالة مؤكدة؛ لا يُلغى من هنا");

        var now = clock.UtcNow;
        var source = new LedgerSource("ReceiptVoucher", voucher.Id, voucher.Number, $"إلغاء سند قبض: {request.Reason.Trim()}");
        // The money must still be in the account (it may have been moved since): an account never goes below zero.
        await accounts.PostAsync(voucher.FinancialAccountId, MoneyDirection.Out, voucher.Amount, AccountTransactionCategory.Reversal, source, now, ct, requireFunds: true);
        if (voucher.CustomerId is { } customerId && voucher.Purpose is ReceiptPurpose.DebtCollection or ReceiptPurpose.CustomerDeposit)
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
            FinanceNames.Receipt(v.Purpose),
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
        if (request.Purpose is { } purpose) query = query.Where(v => v.Purpose == purpose);
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

    public async Task<OwnerExpensesTotalDto> OwnerExpensesTotalAsync(DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var (start, end) = clock.LocalDaysToUtcRange(from ?? new DateOnly(2000, 1, 1), to ?? clock.LocalToday);
        var rows = db.PaymentVouchers.AsNoTracking()
            .Where(v => v.Purpose == PaymentPurpose.OwnerWithdrawal && v.Status == VoucherStatus.Posted && v.Date >= start && v.Date < end);
        return new OwnerExpensesTotalDto(await rows.CountAsync(ct), await rows.SumAsync(v => (decimal?)v.Amount, ct) ?? 0);
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
        // A missing account is a clear message, not a database error.
        if (!await db.FinancialAccounts.AnyAsync(a => a.Id == request.FinancialAccountId && a.IsActive, ct))
            throw new ValidationFailedException("financialAccountId", "الحساب المالي غير موجود أو معطّل");
        Supplier? supplier = null;
        if (request.SupplierId is { } supplierId)
        {
            await db.LockRowAsync("Suppliers", supplierId, ct);
            supplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == supplierId, ct) ?? throw new ValidationFailedException("supplierId", "المورد غير موجود");
            if (request.Purpose == PaymentPurpose.SupplierPayment && !request.Advance && request.Amount > supplier.Balance + 0.009m)
                throw new BusinessRuleException(supplier.Balance <= 0
                    ? $"لا يوجد مبلغ مستحق للمورد «{supplier.Name}»"
                    : $"المبلغ أكبر من المستحق للمورد «{supplier.Name}» ({supplier.Balance:0.00} ₪)");
        }

        // Paying back a customer's credit: never more than the store owes them.
        Customer? customer = null;
        if (request.Purpose == PaymentPurpose.CustomerRefund)
        {
            if (request.CustomerId is { } refundCustomerId) await db.LockRowAsync("Customers", refundCustomerId, ct);
            customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.CustomerId, ct) ?? throw new ValidationFailedException("customerId", "العميل غير موجود");
            if (request.Amount > -customer.Balance + 0.009m)
                throw new BusinessRuleException(customer.Balance >= 0
                    ? $"لا يوجد رصيد للعميل «{customer.Name}» لإعادته"
                    : $"المبلغ أكبر من رصيد العميل «{customer.Name}» ({-customer.Balance:0.00} ₪)");
        }

        var beneficiary = supplier?.Name ?? customer?.Name ?? request.BeneficiaryName!.Trim();
        if (!request.ConfirmDuplicate)
        {
            var since = clock.UtcNow - DuplicateWindow;
            if (await db.PaymentVouchers.AnyAsync(v => v.Status == VoucherStatus.Posted && v.Date >= since && v.Amount == request.Amount
                    && v.FinancialAccountId == request.FinancialAccountId && v.BeneficiaryName == beneficiary, ct))
                throw new ConflictException("سُجّل قبل دقائق سند صرف بنفس المبلغ لنفس المستفيد ونفس الحساب. إذا كانت دفعة جديدة فعلًا، أكّد التسجيل.");
        }

        if (request.Advance && (request.Purpose != PaymentPurpose.SupplierPayment || supplier is null))
            throw new ValidationFailedException("advance", "الدفعة المقدمة تكون لمورد فقط");
        // Only an owner's expense or a supplier advance may be dated back (paid yesterday, recorded today); every other payment is dated now.
        var dated = request.Purpose == PaymentPurpose.OwnerWithdrawal || request.Advance;
        if (request.Date is { } day && day != clock.LocalToday && !dated)
            throw new ValidationFailedException("date", "التاريخ متاح لمصاريف صاحب العمل والدفعات المقدمة للموردين فقط");
        var date = dated ? await DocumentDates.ToUtcAsync(request.Date, clock, db, ct) : clock.UtcNow;

        var voucher = await PostPaymentAsync(new PaymentVoucher
        {
            Date = date,
            Purpose = request.Purpose,
            SupplierId = supplier?.Id,
            CustomerId = customer?.Id,
            BeneficiaryName = beneficiary,
            Amount = request.Amount,
            FinancialAccountId = request.FinancialAccountId,
            Reference = Clean(request.Reference),
            Notes = Clean(request.Notes) ?? (request.Advance ? "دفعة مقدمة للمورد لحين وصول البضاعة" : null),
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
            PaymentPurpose.CustomerRefund => AccountTransactionCategory.SaleRefund,
            PaymentPurpose.EmployeeAdvance => AccountTransactionCategory.EmployeeAdvance,
            PaymentPurpose.OwnerWithdrawal => AccountTransactionCategory.OwnerWithdrawal,
            _ => AccountTransactionCategory.OtherPayment,
        };
        await accounts.PostAsync(voucher.FinancialAccountId, MoneyDirection.Out, voucher.Amount, category, source, voucher.Date, ct, requireFunds: true);
        if (voucher.SupplierId is { } supplierId && voucher.Purpose == PaymentPurpose.SupplierPayment)
            await suppliers.PostAsync(supplierId, SupplierEntryType.Payment, voucher.Amount, 0, source, voucher.Date, ct);
        if (voucher.CustomerId is { } customerId && voucher.Purpose == PaymentPurpose.CustomerRefund)
        {
            // Checked again under the ledger's row lock, so two refunds at once cannot both pass.
            var after = await customers.PostAsync(customerId, CustomerEntryType.Refund, voucher.Amount, 0, source with { Description = "إعادة رصيد للعميل" }, voucher.Date, ct);
            if (after > 0.009m) throw new BusinessRuleException("المبلغ أكبر من رصيد العميل");
        }

        audit.Log("Voucher.Payment", "PaymentVoucher", voucher.Id.ToString(), null, new { voucher.Number, voucher.Amount, voucher.BeneficiaryName, voucher.Purpose });
        return voucher;
    }

    public async Task<VoucherDto> VoidPaymentAsync(int id, VoidDocumentRequest request, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.LockRowAsync("PaymentVouchers", id, ct);
        var voucher = await db.PaymentVouchers.FirstOrDefaultAsync(v => v.Id == id, ct) ?? throw new NotFoundException("السند غير موجود");
        if (voucher.Purpose == PaymentPurpose.Expense) throw new BusinessRuleException("هذا السند لمصروف؛ ألغِ المصروف من شاشة المصروفات");
        if (voucher.Purpose == PaymentPurpose.EmployeeAdvance) throw new BusinessRuleException("هذا السند لسلفة موظف؛ ألغِ السلفة من شاشة الرواتب");
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
        if (voucher.CustomerId is { } customerId && voucher.Purpose == PaymentPurpose.CustomerRefund)
            await customers.PostAsync(customerId, CustomerEntryType.Reversal, 0, voucher.Amount, source, now, ct);

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
        return query.Select(v => new VoucherDto(v.Id, v.Number, v.Date, v.BeneficiaryName, null, v.CustomerId, v.SupplierId, v.Amount,
            v.FinancialAccountId, v.FinancialAccount.Name, v.Purpose.ToString(),
            FinanceNames.Payment(v.Purpose),
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
        // A missing account is a clear message, not a database error.
        if (!await db.FinancialAccounts.AnyAsync(a => a.Id == request.FinancialAccountId && a.IsActive, ct))
            throw new ValidationFailedException("financialAccountId", "الحساب المالي غير موجود أو معطّل");
        var expense = await PostExpenseAsync(category.Id, request.Description.Trim(), request.Amount, request.FinancialAccountId,
            await DocumentDates.ToUtcAsync(request.Date, clock, db, ct), ct, request.AttachmentFileId);

        idempotency.Record(request.IdempotencyKey, operation, "Expense", expense.Id.ToString());
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await ProjectExpenses(db.Expenses.AsNoTracking().Where(e => e.Id == expense.Id)).FirstAsync(ct);
    }

    public async Task<Expense> PostExpenseAsync(int categoryId, string description, decimal amount, int accountId, DateTime date,
        CancellationToken ct = default, Guid? attachmentFileId = null)
    {
        var categoryName = await db.ExpenseCategories.Where(c => c.Id == categoryId).Select(c => c.Name).FirstAsync(ct);

        // The expense is paid through its own voucher: cash flow reads the voucher, profit reads the expense — never both.
        var voucher = await PostPaymentAsync(new PaymentVoucher
        {
            Date = date,
            Purpose = PaymentPurpose.Expense,
            BeneficiaryName = categoryName,
            Amount = amount,
            FinancialAccountId = accountId,
            Notes = description,
            AttachmentFileId = attachmentFileId,
        }, ct);

        var expense = new Expense
        {
            Number = await numbers.NextAsync(DocumentType.Expense, ct),
            Date = date,
            ExpenseCategoryId = categoryId,
            Description = description,
            Amount = amount,
            FinancialAccountId = accountId,
            AttachmentFileId = attachmentFileId,
            PaymentVoucherId = voucher.Id,
        };
        db.Expenses.Add(expense);
        await db.SaveChangesAsync(ct);
        return expense;
    }

    public async Task<ExpenseDto> VoidExpenseAsync(int id, VoidDocumentRequest request, CancellationToken ct = default)
    {
        // A salary or an advance is undone where it was made, so the employee's record stays right.
        if (await db.PayrollEntries.AnyAsync(e => e.ExpenseId == id, ct) || await db.PayrollSettlements.AnyAsync(s => s.ExpenseId == id, ct))
            throw new BusinessRuleException("هذا المصروف من الرواتب؛ ألغِ السلفة أو المخالصة من شاشة الرواتب");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.LockRowAsync("Expenses", id, ct);
        await VoidExpenseCoreAsync(id, request.Reason.Trim(), ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await ProjectExpenses(db.Expenses.AsNoTracking().Where(e => e.Id == id)).FirstAsync(ct);
    }

    public Task<PaymentVoucher> PostAdvanceAsync(string beneficiary, decimal amount, int accountId, DateTime date, string? notes, CancellationToken ct = default) =>
        PostPaymentAsync(new PaymentVoucher
        {
            Date = date,
            Purpose = PaymentPurpose.EmployeeAdvance,
            BeneficiaryName = beneficiary,
            Amount = amount,
            FinancialAccountId = accountId,
            Notes = notes,
        }, ct);

    public async Task VoidPaymentCoreAsync(int voucherId, string reason, CancellationToken ct = default)
    {
        var voucher = await db.PaymentVouchers.FirstOrDefaultAsync(v => v.Id == voucherId, ct) ?? throw new NotFoundException("السند غير موجود");
        await VoidPaymentCoreAsync(voucher, reason, ct);
    }

    public async Task VoidExpenseCoreAsync(int expenseId, string reason, CancellationToken ct = default)
    {
        var expense = await db.Expenses.Include(e => e.PaymentVoucher).FirstOrDefaultAsync(e => e.Id == expenseId, ct) ?? throw new NotFoundException("المصروف غير موجود");
        if (expense.Status == VoucherStatus.Voided) throw new BusinessRuleException("المصروف ملغى من قبل");
        await VoidPaymentCoreAsync(expense.PaymentVoucher, reason, ct);
        expense.Status = VoucherStatus.Voided;
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
