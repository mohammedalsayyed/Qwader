using Microsoft.EntityFrameworkCore;
using Qwader.Application.Catalog;
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
                    Product.BuildDisplayName(l.Product.Name, l.Product.Size, l.Product.Color, l.Product.Model), l.UnitName ?? l.Product.Unit.Name,
                    l.Quantity, l.UnitCost, l.LineDiscount, l.LineTotal, l.NetUnitCost, l.ReturnedQuantity, l.ProductUnitId, l.UnitFactor)).ToList(),
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

        await using var tx = await TxScope.BeginAsync(db, ct);
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
        await db.LockRowAsync("PurchaseInvoices", id, ct);
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
        await db.LockRowAsync("PurchaseInvoices", id, ct);
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

    public Task<PurchaseDetailDto> VoidAsync(int id, VoidRequest request, CancellationToken ct = default) => VoidCoreAsync(id, request, false, ct);

    /// <param name="replacing">Editing: the goods may already be partly sold, so the stock may dip below zero until the new
    /// invoice brings it back (checked at the end of the edit).</param>
    private async Task<PurchaseDetailDto> VoidCoreAsync(int id, VoidRequest request, bool replacing, CancellationToken ct)
    {
        await using var tx = await TxScope.BeginAsync(db, ct);
        await db.LockRowAsync("PurchaseInvoices", id, ct);
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
                await inventory.IssueAsync(new StockEntry(line.ProductId, invoice.WarehouseId, line.Quantity * line.UnitFactor, StockMovementType.PurchaseVoid,
                    "PurchaseInvoice", invoice.Id, invoice.Number, "إلغاء فاتورة شراء"), line.NetUnitCost / line.UnitFactor, ct, allowNegative: replacing);

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

    /// <summary>
    /// Edits a posted purchase: in one transaction the invoice is voided (goods out at its cost, the supplier's balance and the
    /// payment reversed) and the new one is posted from <paramref name="request"/>. Goods already sold may stay sold as long as the
    /// new quantities cover them: no product may end below zero, or nothing happens.
    /// </summary>
    public async Task<PurchaseDetailDto> ReplaceAsync(int id, SavePurchaseRequest request, CancellationToken ct = default)
    {
        if (await idempotency.FindAsync(request.IdempotencyKey, CreateOperation, ct) is { } done)
            return await GetAsync(int.Parse(done.ResultId!), ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.LockRowAsync("PurchaseInvoices", id, ct);
        var original = await db.PurchaseInvoices.AsNoTracking().Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("فاتورة الشراء غير موجودة");
        if (original.Status != PurchaseStatus.Posted) throw new BusinessRuleException("تُعدَّل الفاتورة المعتمدة فقط (المسودة تُعدَّل مباشرة)");
        if (original.Lines.Any(l => l.ReturnedQuantity > 0)) throw new BusinessRuleException("على الفاتورة مردود؛ ألغِ المردود أولًا ثم عدّلها");

        var productIds = original.Lines.Select(l => l.ProductId).Concat(request.Lines.Select(l => l.ProductId)).Distinct().ToList();
        async Task<Dictionary<int, decimal>> BalancesAsync() => await db.StockBalances.AsNoTracking()
            .Where(b => productIds.Contains(b.ProductId) && b.WarehouseId == original.WarehouseId).ToDictionaryAsync(b => b.ProductId, b => b.Quantity, ct);
        var before = await BalancesAsync();

        await VoidCoreAsync(id, new VoidRequest("تعديل الفاتورة"), true, ct);
        var created = await CreateAsync(request with { Post = true }, ct);

        // Whatever was sold from the old quantities must still be covered by the new ones: no product ends below zero
        // (or lower than it already was).
        var after = await BalancesAsync();
        foreach (var (productId, quantity) in after.Where(a => a.Value < 0 && a.Value < before.GetValueOrDefault(a.Key)))
        {
            var name = await db.Products.Where(p => p.Id == productId).Select(p => p.Name).FirstAsync(ct);
            throw new BusinessRuleException($"بِيع من «{name}» أكثر من الكمية الجديدة (ينقص {-quantity:0.###})؛ صحّح الكمية");
        }

        var old = await db.PurchaseInvoices.FirstAsync(p => p.Id == id, ct);
        old.VoidReason = $"تعديل الفاتورة — بدلها {created.Number}";
        var made = await db.PurchaseInvoices.FirstAsync(p => p.Id == created.Id, ct);
        made.Notes = $"بدل الفاتورة {original.Number} (تعديل)" + (string.IsNullOrWhiteSpace(made.Notes) ? "" : $" — {made.Notes}");
        audit.Log("Purchase.Edit", "PurchaseInvoice", created.Id.ToString(), new { original.Number, original.Total }, new { created.Number, created.Total });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(created.Id, CancellationToken.None);
    }

    /// <summary>What does not touch money or stock: the notes and the supplier's own invoice number.</summary>
    public async Task<PurchaseDetailDto> UpdateDetailsAsync(int id, UpdatePurchaseDetailsRequest request, CancellationToken ct = default)
    {
        var invoice = await db.PurchaseInvoices.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("فاتورة الشراء غير موجودة");
        var before = new { invoice.Notes, invoice.SupplierInvoiceNo };
        invoice.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        invoice.SupplierInvoiceNo = string.IsNullOrWhiteSpace(request.SupplierInvoiceNo) ? null : request.SupplierInvoiceNo.Trim();
        audit.Log("Purchase.UpdateDetails", "PurchaseInvoice", id.ToString(), before, new { invoice.Notes, invoice.SupplierInvoiceNo });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<PurchaseDetailDto> ReturnAsync(int id, PurchaseReturnRequest request, CancellationToken ct = default)
    {
        if (await idempotency.FindAsync(request.IdempotencyKey, ReturnOperation, ct) is not null)
            return await GetAsync(id, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.LockRowAsync("PurchaseInvoices", id, ct);
        var invoice = await db.PurchaseInvoices.Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("فاتورة الشراء غير موجودة");
        if (invoice.Status != PurchaseStatus.Posted) throw new BusinessRuleException("يمكن إرجاع البضاعة من فاتورة معتمدة فقط");

        var date = await DocumentDates.ToUtcAsync(request.Date, clock, db, ct);
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

        await QuantityRules.EnsureAsync(db, request.Lines.Select(r => invoice.Lines.FirstOrDefault(l => l.Id == r.PurchaseInvoiceLineId) is { } l
            ? (l.ProductId, l.ProductUnitId, r.Quantity) : (0, (int?)null, 0m)), ct);
        // Cash back from the supplier only when the store owes them nothing: while it owes, the return comes off what it owes
        // (a refund of money never paid would leave the debt and add cash).
        if (request.Settlement == ReturnSettlement.Refund)
        {
            await db.LockRowAsync("Suppliers", invoice.SupplierId, ct);
            var owed = await db.Suppliers.Where(s => s.Id == invoice.SupplierId).Select(s => s.Balance).FirstAsync(ct);
            if (owed > 0.004m)
                throw new BusinessRuleException($"عليك للمورد {owed:0.00} ₪: اختر «خصم من المستحق» بدل استرداد المبلغ");
        }
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
        {
            var factor = invoice.Lines.First(l => l.Id == line.PurchaseInvoiceLineId).UnitFactor;
            await inventory.IssueAsync(new StockEntry(line.ProductId, invoice.WarehouseId, line.Quantity * factor, StockMovementType.PurchaseReturn,
                "PurchaseReturn", ret.Id, ret.Number, ret.Reason), line.UnitCost / factor, ct);
        }

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

        await QuantityRules.EnsureAsync(db, r.Lines.Select(l => (l.ProductId, l.ProductUnitId, l.Quantity)), ct);
        var productIds = r.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, p.IsArchived, Units = p.Units.Select(u => new { u.Id, u.Unit.Name, u.Factor }).ToList() })
            .ToDictionaryAsync(p => p.Id, ct);
        if (products.Count != productIds.Count) throw new ValidationFailedException("lines", "أحد المنتجات غير موجود");
        if (products.Values.Any(p => p.IsArchived)) throw new ValidationFailedException("lines", "لا يمكن شراء منتج مؤرشف؛ ألغِ أرشفته أولًا");

        invoice.SupplierId = r.SupplierId;
        invoice.WarehouseId = r.WarehouseId ?? await db.Warehouses.Where(w => w.IsDefault).Select(w => w.Id).FirstAsync(ct);
        invoice.Date = await DocumentDates.ToUtcAsync(r.Date, clock, db, ct);
        invoice.SupplierInvoiceNo = string.IsNullOrWhiteSpace(r.SupplierInvoiceNo) ? null : r.SupplierInvoiceNo.Trim();
        invoice.Notes = string.IsNullOrWhiteSpace(r.Notes) ? null : r.Notes.Trim();

        var lines = r.Lines.Select(l =>
        {
            var gross = CostMath.RoundMoney(l.Quantity * l.UnitCost);
            if (l.LineDiscount > gross) throw new ValidationFailedException("lines", "خصم البند أكبر من قيمته");
            var unit = l.ProductUnitId is { } unitId
                ? products[l.ProductId].Units.FirstOrDefault(u => u.Id == unitId)
                    ?? throw new ValidationFailedException("lines", $"الوحدة المختارة لـ«{products[l.ProductId].Name}» لم تعد موجودة؛ أعد اختيارها")
                : null;
            return new PurchaseInvoiceLine
            {
                ProductId = l.ProductId, Quantity = l.Quantity, UnitCost = l.UnitCost, LineDiscount = l.LineDiscount, LineTotal = gross - l.LineDiscount,
                ProductUnitId = unit?.Id, UnitName = unit?.Name, UnitFactor = unit?.Factor ?? 1,
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
        // A product archived after the draft was saved is not bought into stock again (its stock was written off at archiving).
        var lineProducts = invoice.Lines.Select(l => l.ProductId).Distinct().ToList();
        var archived = await db.Products.AsNoTracking().Where(p => lineProducts.Contains(p.Id) && p.IsArchived).Select(p => p.Name).FirstOrDefaultAsync(ct);
        if (archived is not null) throw new BusinessRuleException($"المنتج «{archived}» مؤرشف؛ ألغِ أرشفته أو احذفه من الفاتورة ثم اعتمدها");
        // Ascending product order keeps lock acquisition consistent across concurrent postings.
        foreach (var line in invoice.Lines.OrderBy(l => l.ProductId).ThenBy(l => l.Id))
        {
            // Stock and cost stay in the base unit: a dozen bought at 120 is 12 pieces at 10.
            await inventory.ReceiveAsync(new StockEntry(line.ProductId, invoice.WarehouseId, line.Quantity * line.UnitFactor, StockMovementType.Purchase,
                "PurchaseInvoice", invoice.Id, invoice.Number), line.NetUnitCost / line.UnitFactor, ct);
            await db.Database.ExecuteSqlAsync($"UPDATE Products SET PurchasePrice = {db.Database.Exact(CostMath.RoundCost(line.UnitCost / line.UnitFactor))} WHERE Id = {line.ProductId}", ct);
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
