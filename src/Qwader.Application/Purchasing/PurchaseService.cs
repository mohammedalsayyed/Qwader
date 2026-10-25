using Microsoft.EntityFrameworkCore;
using Qwader.Application.Abstractions;
using Qwader.Application.Common;
using Qwader.Application.Ledgers;
using Qwader.Domain.Administration;
using Qwader.Domain.Catalog;
using Qwader.Domain.Finance;
using Qwader.Domain.Inventory;
using Qwader.Domain.Purchasing;

namespace Qwader.Application.Purchasing;

public sealed class PurchaseService(
    IAppDbContext db,
    IInventoryLedger inventory,
    IAccountLedger accounts,
    ISupplierLedger suppliers,
    IDocumentNumberGenerator numbers,
    IIdempotencyService idempotency,
    ICurrentUser currentUser,
    IClock clock,
    IAuditService audit) : IPurchaseService
{
    private const string CreateOperation = "Purchase.Create";
    private const string PostOperation = "Purchase.Post";
    private const string ReturnOperation = "Purchase.Return";

    public async Task<PagedResult<PurchaseListItemDto>> ListAsync(PurchaseListRequest request, CancellationToken ct = default)
    {
        var query = db.PurchaseInvoices.AsNoTracking();
        if (request.SupplierId is { } supplierId) query = query.Where(p => p.SupplierId == supplierId);
        if (request.Status is { } status) query = query.Where(p => p.Status == status);
        if (request.From is not null || request.To is not null)
        {
            var (start, end) = clock.LocalDaysToUtcRange(request.From ?? new DateOnly(2000, 1, 1), request.To ?? clock.LocalToday);
            query = query.Where(p => p.Date >= start && p.Date < end);
        }
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var raw = request.Search.Trim();
            var term = ArabicText.Normalize(raw);
            query = query.Where(p => p.Number.Contains(raw) || p.SupplierInvoiceNo == raw || p.Supplier.NormalizedName.Contains(term));
        }

        return await query.OrderByDescending(p => p.Date).ThenByDescending(p => p.Id)
            .Select(p => new PurchaseListItemDto(p.Id, p.Number, p.Date, p.SupplierId, p.Supplier.Name, p.SupplierInvoiceNo, p.Status,
                p.Total, p.PaidAmount,
                db.PurchaseReturns.Where(r => r.PurchaseInvoiceId == p.Id).Sum(r => (decimal?)r.Total) ?? 0,
                p.Lines.Count))
            .ToPagedAsync(request, ct);
    }

    public async Task<PurchaseDetailDto> GetAsync(int id, CancellationToken ct = default)
    {
        var p = await db.PurchaseInvoices.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new
            {
                Invoice = x,
                SupplierName = x.Supplier.Name,
                WarehouseName = x.Warehouse.Name,
                AccountName = x.PaymentAccount != null ? x.PaymentAccount.Name : null,
                CreatedBy = db.Users.Where(u => u.Id == x.CreatedById).Select(u => u.FullName).FirstOrDefault(),
                Lines = x.Lines.OrderBy(l => l.Id).Select(l => new PurchaseLineDto(l.Id, l.ProductId, l.Product.Sku,
                    Product.BuildDisplayName(l.Product.Name, l.Product.Size, l.Product.Color, l.Product.Model), l.Product.Unit.Name,
                    l.Quantity, l.UnitCost, l.LineDiscount, l.LineTotal, l.NetUnitCost, l.ReturnedQuantity)).ToList(),
            })
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("فاتورة الشراء غير موجودة");

        var returns = await db.PurchaseReturns.AsNoTracking().Where(r => r.PurchaseInvoiceId == id).OrderBy(r => r.Id)
            .Select(r => new PurchaseReturnSummaryDto(r.Id, r.Number, r.Date, r.Total, r.Settlement, r.Reason)).ToListAsync(ct);

        var i = p.Invoice;
        return new PurchaseDetailDto(i.Id, i.Number, i.Date, i.Status, i.SupplierId, p.SupplierName, i.SupplierInvoiceNo, i.WarehouseId, p.WarehouseName,
            i.Subtotal, i.Discount, i.Total, i.PaidAmount, i.Total - i.PaidAmount, i.PaymentAccountId, p.AccountName, i.Notes,
            p.CreatedBy, i.CreatedAt, i.PostedAt, i.VoidedAt, i.VoidReason, p.Lines, returns);
    }

    public async Task<PurchaseDetailDto> CreateAsync(SavePurchaseRequest request, CancellationToken ct = default)
    {
        if (await idempotency.FindAsync(request.IdempotencyKey, CreateOperation, ct) is { } done)
            return await GetAsync(int.Parse(done.ResultId!), ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var invoice = new PurchaseInvoice { Number = await numbers.NextAsync(DocumentType.PurchaseInvoice, ct) };
        await ApplyAsync(invoice, request, ct);
        db.PurchaseInvoices.Add(invoice);
        await db.SaveChangesAsync(ct);

        if (request.Post) await PostInternalAsync(invoice, ct);

        idempotency.Record(request.IdempotencyKey, CreateOperation, "PurchaseInvoice", invoice.Id.ToString());
        audit.Log(request.Post ? "Purchase.CreateAndPost" : "Purchase.CreateDraft", "PurchaseInvoice", invoice.Id.ToString(), null,
            new { invoice.Number, invoice.SupplierId, invoice.Total, invoice.PaidAmount });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(invoice.Id, ct);
    }

    public async Task<PurchaseDetailDto> UpdateDraftAsync(int id, SavePurchaseRequest request, CancellationToken ct = default)
    {
        if (request.Post && await idempotency.FindAsync(request.IdempotencyKey, PostOperation, ct) is not null)
            return await GetAsync(id, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var invoice = await db.PurchaseInvoices.Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("فاتورة الشراء غير موجودة");
        if (invoice.Status != PurchaseStatus.Draft) throw new BusinessRuleException("لا يمكن تعديل فاتورة معتمدة أو ملغاة");

        db.PurchaseInvoiceLines.RemoveRange(invoice.Lines);
        invoice.Lines.Clear();
        await ApplyAsync(invoice, request, ct);
        await db.SaveChangesAsync(ct);

        if (request.Post)
        {
            await PostInternalAsync(invoice, ct);
            idempotency.Record(request.IdempotencyKey, PostOperation, "PurchaseInvoice", invoice.Id.ToString());
        }
        audit.Log(request.Post ? "Purchase.Post" : "Purchase.UpdateDraft", "PurchaseInvoice", id.ToString(), null, new { invoice.Total, invoice.PaidAmount });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<PurchaseDetailDto> PostAsync(int id, PostPurchaseRequest request, CancellationToken ct = default)
    {
        if (await idempotency.FindAsync(request.IdempotencyKey, PostOperation, ct) is not null)
            return await GetAsync(id, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var invoice = await db.PurchaseInvoices.Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("فاتورة الشراء غير موجودة");
        if (invoice.Status != PurchaseStatus.Draft) throw new BusinessRuleException("هذه الفاتورة معتمدة أو ملغاة من قبل");

        await PostInternalAsync(invoice, ct);
        idempotency.Record(request.IdempotencyKey, PostOperation, "PurchaseInvoice", id.ToString());
        audit.Log("Purchase.Post", "PurchaseInvoice", id.ToString(), null, new { invoice.Number, invoice.Total, invoice.PaidAmount });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<PurchaseDetailDto> VoidAsync(int id, VoidRequest request, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var invoice = await db.PurchaseInvoices.Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("فاتورة الشراء غير موجودة");
        if (invoice.Status == PurchaseStatus.Voided) throw new BusinessRuleException("الفاتورة ملغاة من قبل");
        if (invoice.Lines.Any(l => l.ReturnedQuantity > 0))
            throw new BusinessRuleException("لا يمكن إلغاء فاتورة عليها مردودات؛ أرجِع باقي الكميات بدلًا من الإلغاء");

        var wasPosted = invoice.Status == PurchaseStatus.Posted;
        if (wasPosted)
        {
            var source = new LedgerSource("PurchaseInvoice", invoice.Id, invoice.Number, $"إلغاء فاتورة الشراء: {request.Reason.Trim()}");
            foreach (var line in invoice.Lines.OrderBy(l => l.ProductId))
                await inventory.IssueAsync(new StockEntry(line.ProductId, invoice.WarehouseId, line.Quantity, StockMovementType.PurchaseVoid,
                    "PurchaseInvoice", invoice.Id, invoice.Number, "إلغاء فاتورة شراء"), line.NetUnitCost, ct);

            var now = clock.UtcNow;
            if (invoice.Total > 0) await suppliers.PostAsync(invoice.SupplierId, SupplierEntryType.Reversal, invoice.Total, 0, source, now, ct);
            if (invoice.PaidAmount > 0)
            {
                await suppliers.PostAsync(invoice.SupplierId, SupplierEntryType.Reversal, 0, invoice.PaidAmount, source, now, ct);
                await accounts.PostAsync(invoice.PaymentAccountId!.Value, MoneyDirection.In, invoice.PaidAmount, AccountTransactionCategory.Reversal, source, now, ct);
            }
        }

        invoice.Status = PurchaseStatus.Voided;
        invoice.VoidedAt = clock.UtcNow;
        invoice.VoidedById = currentUser.UserId;
        invoice.VoidReason = request.Reason.Trim();
        audit.Log("Purchase.Void", "PurchaseInvoice", id.ToString(), new { WasPosted = wasPosted }, new { invoice.Number }, request.Reason);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<PurchaseDetailDto> ReturnAsync(int id, PurchaseReturnRequest request, CancellationToken ct = default)
    {
        if (await idempotency.FindAsync(request.IdempotencyKey, ReturnOperation, ct) is not null)
            return await GetAsync(id, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var invoice = await db.PurchaseInvoices.Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("فاتورة الشراء غير موجودة");
        if (invoice.Status != PurchaseStatus.Posted) throw new BusinessRuleException("يمكن إرجاع البضاعة من فاتورة معتمدة فقط");

        var date = DocumentDates.ToUtc(request.Date, clock);
        var ret = new PurchaseReturn
        {
            Number = await numbers.NextAsync(DocumentType.PurchaseReturn, ct),
            PurchaseInvoiceId = invoice.Id,
            SupplierId = invoice.SupplierId,
            Date = date,
            Settlement = request.Settlement,
            RefundAccountId = request.Settlement == ReturnSettlement.Refund ? request.RefundAccountId : null,
            Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(),
        };

        foreach (var input in request.Lines.GroupBy(l => l.PurchaseInvoiceLineId).Select(g => new { LineId = g.Key, Quantity = g.Sum(x => x.Quantity) }))
        {
            var line = invoice.Lines.FirstOrDefault(l => l.Id == input.LineId)
                ?? throw new ValidationFailedException("lines", "أحد البنود لا ينتمي لهذه الفاتورة");
            var returnable = line.Quantity - line.ReturnedQuantity;
            if (input.Quantity > returnable)
                throw new BusinessRuleException($"الكمية المرتجعة أكبر من المسموح ({returnable:0.###}) لأحد البنود");

            line.ReturnedQuantity += input.Quantity;
            var lineTotal = CostMath.RoundMoney(input.Quantity * line.NetUnitCost);
            ret.Lines.Add(new PurchaseReturnLine
            {
                PurchaseInvoiceLineId = line.Id, ProductId = line.ProductId, Quantity = input.Quantity, UnitCost = line.NetUnitCost, LineTotal = lineTotal,
            });
        }
        ret.Total = ret.Lines.Sum(l => l.LineTotal);
        db.PurchaseReturns.Add(ret);
        await db.SaveChangesAsync(ct);

        foreach (var line in ret.Lines.OrderBy(l => l.ProductId))
            await inventory.IssueAsync(new StockEntry(line.ProductId, invoice.WarehouseId, line.Quantity, StockMovementType.PurchaseReturn,
                "PurchaseReturn", ret.Id, ret.Number, ret.Reason), line.UnitCost, ct);

        var source = new LedgerSource("PurchaseReturn", ret.Id, ret.Number, $"مردود من الفاتورة {invoice.Number}");
        if (ret.Total > 0)
        {
            await suppliers.PostAsync(invoice.SupplierId, SupplierEntryType.Return, ret.Total, 0, source, date, ct);
            if (request.Settlement == ReturnSettlement.Refund)
            {
                await suppliers.PostAsync(invoice.SupplierId, SupplierEntryType.Refund, 0, ret.Total, source, date, ct);
                await accounts.PostAsync(request.RefundAccountId!.Value, MoneyDirection.In, ret.Total, AccountTransactionCategory.SupplierRefund, source, date, ct);
            }
        }

        idempotency.Record(request.IdempotencyKey, ReturnOperation, "PurchaseReturn", ret.Id.ToString());
        audit.Log("Purchase.Return", "PurchaseInvoice", id.ToString(), null, new { ret.Number, ret.Total, request.Settlement });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Validates references and recomputes every amount server-side; client totals are never trusted.</summary>
    private async Task ApplyAsync(PurchaseInvoice invoice, SavePurchaseRequest r, CancellationToken ct)
    {
        var supplier = await db.Suppliers.AsNoTracking().Where(s => s.Id == r.SupplierId).Select(s => new { s.IsActive }).FirstOrDefaultAsync(ct)
            ?? throw new ValidationFailedException("supplierId", "المورد غير موجود");
        if (!supplier.IsActive) throw new ValidationFailedException("supplierId", "المورد غير فعّال");

        var productIds = r.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).Select(p => new { p.Id, p.IsArchived }).ToListAsync(ct);
        if (products.Count != productIds.Count) throw new ValidationFailedException("lines", "أحد المنتجات غير موجود");
        if (products.Any(p => p.IsArchived)) throw new ValidationFailedException("lines", "لا يمكن شراء منتج مؤرشف؛ ألغِ أرشفته أولًا");

        invoice.SupplierId = r.SupplierId;
        invoice.WarehouseId = r.WarehouseId ?? await db.Warehouses.Where(w => w.IsDefault).Select(w => w.Id).FirstAsync(ct);
        invoice.Date = DocumentDates.ToUtc(r.Date, clock);
        invoice.SupplierInvoiceNo = string.IsNullOrWhiteSpace(r.SupplierInvoiceNo) ? null : r.SupplierInvoiceNo.Trim();
        invoice.Notes = string.IsNullOrWhiteSpace(r.Notes) ? null : r.Notes.Trim();

        var lines = r.Lines.Select(l =>
        {
            var gross = CostMath.RoundMoney(l.Quantity * l.UnitCost);
            if (l.LineDiscount > gross) throw new ValidationFailedException("lines", "خصم البند أكبر من قيمته");
            return new PurchaseInvoiceLine
            {
                ProductId = l.ProductId, Quantity = l.Quantity, UnitCost = l.UnitCost, LineDiscount = l.LineDiscount, LineTotal = gross - l.LineDiscount,
            };
        }).ToList();

        invoice.Subtotal = lines.Sum(l => l.LineTotal);
        if (r.Discount > invoice.Subtotal) throw new ValidationFailedException("discount", "الخصم أكبر من إجمالي الفاتورة");
        invoice.Discount = r.Discount;
        invoice.Total = invoice.Subtotal - r.Discount;
        if (r.PaidAmount > invoice.Total) throw new ValidationFailedException("paidAmount", "المبلغ المدفوع أكبر من إجمالي الفاتورة");
        invoice.PaidAmount = r.PaidAmount;
        invoice.PaymentAccountId = r.PaidAmount > 0 ? r.PaymentAccountId : null;
        if (invoice.PaymentAccountId is { } accountId && !await db.FinancialAccounts.AnyAsync(a => a.Id == accountId && a.IsActive, ct))
            throw new ValidationFailedException("paymentAccountId", "الحساب المالي غير موجود أو معطّل");

        var shares = CostMath.AllocateDiscount(lines.Select(l => l.LineTotal).ToList(), invoice.Discount);
        for (var i = 0; i < lines.Count; i++)
        {
            lines[i].NetUnitCost = CostMath.RoundCost((lines[i].LineTotal - shares[i]) / lines[i].Quantity);
            invoice.Lines.Add(lines[i]);
        }
    }

    private async Task PostInternalAsync(PurchaseInvoice invoice, CancellationToken ct)
    {
        // Ascending product order keeps lock acquisition consistent across concurrent postings.
        foreach (var line in invoice.Lines.OrderBy(l => l.ProductId).ThenBy(l => l.Id))
        {
            await inventory.ReceiveAsync(new StockEntry(line.ProductId, invoice.WarehouseId, line.Quantity, StockMovementType.Purchase,
                "PurchaseInvoice", invoice.Id, invoice.Number), line.NetUnitCost, ct);
            await db.Database.ExecuteSqlAsync($"UPDATE Products SET PurchasePrice = {line.UnitCost} WHERE Id = {line.ProductId}", ct);
        }

        var source = new LedgerSource("PurchaseInvoice", invoice.Id, invoice.Number, invoice.SupplierInvoiceNo is null ? null : $"فاتورة المورد {invoice.SupplierInvoiceNo}");
        if (invoice.Total > 0)
            await suppliers.PostAsync(invoice.SupplierId, SupplierEntryType.Purchase, 0, invoice.Total, source, invoice.Date, ct);
        if (invoice.PaidAmount > 0)
        {
            await suppliers.PostAsync(invoice.SupplierId, SupplierEntryType.Payment, invoice.PaidAmount, 0, source with { Description = "دفعة عند الشراء" }, invoice.Date, ct);
            await accounts.PostAsync(invoice.PaymentAccountId!.Value, MoneyDirection.Out, invoice.PaidAmount, AccountTransactionCategory.SupplierPayment,
                source with { Description = "دفعة لفاتورة شراء" }, invoice.Date, ct, requireFunds: true);
        }

        invoice.Status = PurchaseStatus.Posted;
        invoice.PostedAt = clock.UtcNow;
        invoice.PostedById = currentUser.UserId;
    }
}
