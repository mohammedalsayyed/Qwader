using Microsoft.EntityFrameworkCore;
using Qwader.Application.Abstractions;
using Qwader.Application.Common;
using Qwader.Application.Customers;
using Qwader.Application.Ledgers;
using Qwader.Application.Purchasing;
using Qwader.Application.Settings;
using Qwader.Domain.Administration;
using Qwader.Domain.Catalog;
using Qwader.Domain.Finance;
using Qwader.Domain.Identity;
using Qwader.Domain.Inventory;
using Qwader.Domain.Purchasing;
using Qwader.Domain.Sales;

namespace Qwader.Application.Sales;

public sealed class SaleService(
    IAppDbContext db,
    IInventoryLedger inventory,
    IAccountLedger accounts,
    ICustomerLedger customers,
    ICustomerService customerService,
    ISettingsService settings,
    IDocumentNumberGenerator numbers,
    IIdempotencyService idempotency,
    ICurrentUser currentUser,
    IClock clock,
    IAuditService audit) : ISaleService
{
    private const string CreateOperation = "Sale.Create";
    private const string ReturnOperation = "Sale.Return";

    private bool CanSeeAll => currentUser.HasPermission(Permissions.SalesViewAll) || currentUser.HasPermission(Permissions.ReportsSales);
    private bool CanSeeProfit => currentUser.HasPermission(Permissions.ReportsProfit) || currentUser.HasPermission(Permissions.ProductsViewCost);

    // ------------------------------------------------------------------ create

    public async Task<SaleDetailDto> CreateAsync(CreateSaleRequest request, CancellationToken ct = default)
    {
        // A double tap or a retried request after a dropped connection returns the invoice already made.
        if (await idempotency.FindAsync(request.IdempotencyKey, CreateOperation, ct) is { } done)
            return await GetAsync(int.Parse(done.ResultId!), ct);

        var productIds = request.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, p.Size, p.Color, p.Model, p.IsArchived, p.RetailPrice, p.WholesalePrice, p.MinSalePrice, p.AverageCost })
            .ToDictionaryAsync(p => p.Id, ct);
        if (products.Count != productIds.Count) throw new ValidationFailedException("lines", "أحد المنتجات غير موجود");
        if (products.Values.FirstOrDefault(p => p.IsArchived) is { } archived)
            throw new BusinessRuleException($"المنتج «{archived.Name}» مؤرشف ولا يمكن بيعه");

        // ---- amounts (always recomputed here; client totals are never trusted)
        var lines = request.Lines.Select(l =>
        {
            var p = products[l.ProductId];
            var gross = CostMath.RoundMoney(l.Quantity * l.UnitPrice);
            if (l.LineDiscount > gross) throw new ValidationFailedException("lines", $"خصم «{p.Name}» أكبر من قيمته");
            return new SaleInvoiceLine
            {
                ProductId = l.ProductId,
                ProductName = Product.BuildDisplayName(p.Name, p.Size, p.Color, p.Model),
                Quantity = l.Quantity,
                UnitPrice = l.UnitPrice,
                LineDiscount = l.LineDiscount,
                LineTotal = gross - l.LineDiscount,
            };
        }).ToList();

        var grossTotal = lines.Sum(l => CostMath.RoundMoney(l.Quantity * l.UnitPrice));
        var lineDiscounts = lines.Sum(l => l.LineDiscount);
        var subtotal = lines.Sum(l => l.LineTotal);
        if (request.InvoiceDiscount > subtotal) throw new ValidationFailedException("invoiceDiscount", "الخصم أكبر من إجمالي الفاتورة");
        var total = subtotal - request.InvoiceDiscount;

        var shares = CostMath.AllocateDiscount(lines.Select(l => l.LineTotal).ToList(), request.InvoiceDiscount);
        for (var i = 0; i < lines.Count; i++) lines[i].NetTotal = lines[i].LineTotal - shares[i];

        // ---- price & discount rules
        var listTotal = request.Lines.Sum(l =>
        {
            var p = products[l.ProductId];
            return CostMath.RoundMoney(l.Quantity * (request.PriceTier == PriceTier.Wholesale ? p.WholesalePrice : p.RetailPrice));
        });
        var discountGiven = Math.Max(0, listTotal - total);
        var canOverride = currentUser.HasPermission(Permissions.SalesPriceOverride);
        if (discountGiven > 0.009m)
        {
            if (!currentUser.HasPermission(Permissions.SalesDiscount) && !canOverride)
                throw new ForbiddenException("ليست لديك صلاحية البيع بخصم أو بأقل من السعر المحدد");
            var maxPercent = (await settings.GetAsync(ct)).MaxDiscountPercent;
            if (!canOverride && listTotal > 0 && discountGiven / listTotal * 100 > maxPercent + 0.001m)
                throw new BusinessRuleException($"الخصم ({Math.Round(discountGiven / listTotal * 100, 1)}%) يتجاوز الحد المسموح ({maxPercent}%)");
        }

        var overridden = new List<string>();
        foreach (var line in lines)
        {
            var p = products[line.ProductId];
            var netUnit = line.NetTotal / line.Quantity;
            var floor = Math.Max(p.MinSalePrice, p.AverageCost);
            if (netUnit + 0.0001m < floor)
            {
                if (!canOverride)
                    throw new BusinessRuleException(netUnit < p.AverageCost
                        ? $"سعر «{line.ProductName}» بعد الخصم أقل من التكلفة؛ يحتاج صلاحية خاصة"
                        : $"سعر «{line.ProductName}» بعد الخصم أقل من الحد الأدنى ({p.MinSalePrice:0.##} ₪)");
                line.PriceOverridden = true;
                overridden.Add(line.ProductName);
            }
        }

        // ---- payments
        var paid = request.Payments.Sum(p => p.Amount);
        if (paid > total) throw new ValidationFailedException("payments", "مجموع الدفعات أكبر من إجمالي الفاتورة؛ سجّل المبلغ المستحق فقط واحسب الباقي للزبون");
        var accountIds = request.Payments.Select(p => p.AccountId).Distinct().ToList();
        if (accountIds.Count > 0 && await db.FinancialAccounts.CountAsync(a => accountIds.Contains(a.Id) && a.IsActive, ct) != accountIds.Count)
            throw new ValidationFailedException("payments", "أحد الحسابات المختارة غير موجود أو معطّل");

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // ---- customer
        Customer customer;
        if (request.CustomerId is { } customerId)
        {
            customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, ct) ?? throw new ValidationFailedException("customerId", "العميل غير موجود");
            if (!customer.IsActive) throw new ValidationFailedException("customerId", "العميل غير فعّال");
        }
        else if (!string.IsNullOrWhiteSpace(request.CustomerPhone))
            customer = await customerService.FindOrCreateByPhoneAsync(request.CustomerName!, request.CustomerPhone, ct);
        else
            customer = await db.Customers.FirstAsync(c => c.IsWalkIn, ct);

        var remaining = total - paid;
        if (remaining > 0 && customer.IsWalkIn)
            throw new BusinessRuleException("المبلغ المدفوع أقل من الإجمالي. للبيع الآجل أو الجزئي اكتب اسم العميل ورقم جواله");

        var now = clock.UtcNow;
        var invoice = new SaleInvoice
        {
            Number = await numbers.NextAsync(DocumentType.SaleInvoice, ct),
            Date = now,
            CustomerId = customer.Id,
            CustomerName = customer.IsWalkIn && !string.IsNullOrWhiteSpace(request.CustomerName) ? request.CustomerName.Trim() : customer.Name,
            CustomerPhone = customer.Phone,
            WarehouseId = await db.Warehouses.Where(w => w.IsDefault).Select(w => w.Id).FirstAsync(ct),
            PriceTier = request.PriceTier,
            GrossTotal = grossTotal,
            LineDiscountTotal = lineDiscounts,
            InvoiceDiscount = request.InvoiceDiscount,
            Total = total,
            PaidAmount = paid,
            SalespersonId = currentUser.RequiredUserId,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
        };
        foreach (var line in lines) invoice.Lines.Add(line);
        foreach (var p in request.Payments)
            invoice.Payments.Add(new SalePayment { FinancialAccountId = p.AccountId, Amount = p.Amount, Reference = string.IsNullOrWhiteSpace(p.Reference) ? null : p.Reference.Trim() });
        db.SaleInvoices.Add(invoice);
        await db.SaveChangesAsync(ct);

        // ---- stock: ascending product order keeps lock order consistent between concurrent sales
        foreach (var line in invoice.Lines.OrderBy(l => l.ProductId).ThenBy(l => l.Id))
        {
            var posting = await inventory.IssueAsync(new StockEntry(line.ProductId, invoice.WarehouseId, line.Quantity, StockMovementType.Sale,
                "SaleInvoice", invoice.Id, invoice.Number), ct: ct);
            line.UnitCost = posting.UnitCost;
        }
        invoice.TotalCost = invoice.Lines.Sum(l => l.Quantity * l.UnitCost);

        // ---- money & receivables
        var source = new LedgerSource("SaleInvoice", invoice.Id, invoice.Number, $"فاتورة بيع — {invoice.CustomerName}");
        foreach (var payment in invoice.Payments)
            await accounts.PostAsync(payment.FinancialAccountId, MoneyDirection.In, payment.Amount, AccountTransactionCategory.SaleReceipt, source, now, ct);

        if (!customer.IsWalkIn)
        {
            await customers.PostAsync(customer.Id, CustomerEntryType.Sale, total, 0, source, now, ct);
            if (paid > 0) await customers.PostAsync(customer.Id, CustomerEntryType.Payment, 0, paid, source with { Description = "مدفوع عند البيع" }, now, ct);
        }

        idempotency.Record(request.IdempotencyKey, CreateOperation, "SaleInvoice", invoice.Id.ToString());
        if (overridden.Count > 0)
            audit.Log("Sale.PriceOverride", "SaleInvoice", invoice.Id.ToString(), null, new { invoice.Number, Products = overridden });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(invoice.Id, ct);
    }

    // ------------------------------------------------------------------ read

    public async Task<PagedResult<SaleListItemDto>> ListAsync(SaleListRequest request, CancellationToken ct = default)
    {
        var query = db.SaleInvoices.AsNoTracking();
        if (!CanSeeAll) query = query.Where(s => s.SalespersonId == currentUser.UserId);
        if (request.Status is { } status) query = query.Where(s => s.Status == status);
        if (request.CustomerId is { } customerId) query = query.Where(s => s.CustomerId == customerId);
        if (request.Unpaid == true) query = query.Where(s => s.PaidAmount < s.Total && s.Status != SaleStatus.Voided);
        if (request.From is not null || request.To is not null)
        {
            var (start, end) = clock.LocalDaysToUtcRange(request.From ?? new DateOnly(2000, 1, 1), request.To ?? clock.LocalToday);
            query = query.Where(s => s.Date >= start && s.Date < end);
        }
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var raw = request.Search.Trim();
            var term = ArabicText.Normalize(raw);
            var phone = PhoneNumbers.Normalize(raw);
            query = query.Where(s => s.Number.EndsWith(raw) || s.Customer.NormalizedName.Contains(term)
                || (phone != null && s.CustomerPhone!.Contains(phone)));
        }

        var users = db.Users;
        return await query.OrderByDescending(s => s.Id)
            .Select(s => new SaleListItemDto(s.Id, s.Number, s.Date, s.CustomerName, s.CustomerPhone, s.Customer.IsWalkIn,
                s.Total, s.PaidAmount,
                db.SaleReturns.Where(r => r.SaleInvoiceId == s.Id).Sum(r => (decimal?)r.Total) ?? 0,
                s.Status,
                users.Where(u => u.Id == s.SalespersonId).Select(u => u.FullName).FirstOrDefault() ?? "",
                s.Lines.Count))
            .ToPagedAsync(request, ct);
    }

    public async Task<SaleDetailDto> GetAsync(int id, CancellationToken ct = default)
    {
        var s = await db.SaleInvoices.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new
            {
                Invoice = x,
                x.Customer.IsWalkIn,
                Salesperson = db.Users.Where(u => u.Id == x.SalespersonId).Select(u => u.FullName).FirstOrDefault(),
                Lines = x.Lines.OrderBy(l => l.Id).Select(l => new
                {
                    l.Id, l.ProductId, l.Product.Sku, l.ProductName, Unit = l.Product.Unit.Name, l.Quantity, l.UnitPrice, l.LineDiscount,
                    l.LineTotal, l.NetTotal, l.ReturnedQuantity, l.UnitCost, l.PriceOverridden,
                }).ToList(),
                Payments = x.Payments.OrderBy(p => p.Id)
                    .Select(p => new SalePaymentDto(p.Id, p.FinancialAccountId, p.FinancialAccount.Name, p.Amount, p.Reference)).ToList(),
            })
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("الفاتورة غير موجودة");

        var invoice = s.Invoice;
        if (!CanSeeAll && invoice.SalespersonId != currentUser.UserId) throw new NotFoundException("الفاتورة غير موجودة");

        var returns = await db.SaleReturns.AsNoTracking().Where(r => r.SaleInvoiceId == id).OrderBy(r => r.Id)
            .Select(r => new SaleReturnSummaryDto(r.Id, r.Number, r.Date, r.Total, r.Settlement, r.Reason)).ToListAsync(ct);
        var returnedCost = await db.SaleReturns.Where(r => r.SaleInvoiceId == id).SumAsync(r => (decimal?)r.Lines
            .Where(l => l.Condition == ReturnCondition.Sellable).Sum(l => l.Quantity * l.UnitCost), ct) ?? 0;

        var showCost = CanSeeProfit;
        var returnedTotal = returns.Sum(r => r.Total);
        decimal? cost = showCost ? CostMath.RoundMoney(invoice.TotalCost - returnedCost) : null;
        decimal? profit = showCost && invoice.Status != SaleStatus.Voided ? CostMath.RoundMoney(invoice.Total - returnedTotal - cost!.Value) : null;

        return new SaleDetailDto(invoice.Id, invoice.Number, invoice.Date, invoice.Status, invoice.PriceTier,
            invoice.CustomerId, invoice.CustomerName, invoice.CustomerPhone, s.IsWalkIn,
            invoice.GrossTotal, invoice.LineDiscountTotal, invoice.InvoiceDiscount, invoice.Total, invoice.PaidAmount, invoice.Total - invoice.PaidAmount,
            returnedTotal, cost, profit, s.Salesperson ?? "", invoice.Notes, invoice.VoidedAt, invoice.VoidReason,
            s.Lines.Select(l => new SaleLineDto(l.Id, l.ProductId, l.Sku, l.ProductName, l.Unit, l.Quantity, l.UnitPrice, l.LineDiscount,
                l.LineTotal, l.NetTotal, l.ReturnedQuantity, showCost ? l.UnitCost : null, l.PriceOverridden)).ToList(),
            s.Payments, returns);
    }

    // ------------------------------------------------------------------ returns

    public async Task<SaleDetailDto> ReturnAsync(int id, SaleReturnRequest request, CancellationToken ct = default)
    {
        if (await idempotency.FindAsync(request.IdempotencyKey, ReturnOperation, ct) is not null) return await GetAsync(id, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var invoice = await db.SaleInvoices.Include(s => s.Lines).Include(s => s.Customer).FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException("الفاتورة غير موجودة");
        if (invoice.Status is SaleStatus.Voided or SaleStatus.Returned) throw new BusinessRuleException("لا توجد كميات قابلة للإرجاع في هذه الفاتورة");
        if (invoice.Customer.IsWalkIn && request.Settlement != ReturnSettlement.Refund)
            throw new BusinessRuleException("فاتورة «زبون نقدي» تُسترد نقدًا فقط");
        if (request.Settlement == ReturnSettlement.Refund
            && !await db.FinancialAccounts.AnyAsync(a => a.Id == request.RefundAccountId && a.IsActive, ct))
            throw new ValidationFailedException("refundAccountId", "الحساب المختار غير موجود أو معطّل");

        var now = clock.UtcNow;
        var ret = new SaleReturn
        {
            Number = await numbers.NextAsync(DocumentType.SaleReturn, ct),
            SaleInvoiceId = invoice.Id,
            CustomerId = invoice.CustomerId,
            Date = now,
            Settlement = request.Settlement,
            RefundAccountId = request.Settlement == ReturnSettlement.Refund ? request.RefundAccountId : null,
            Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(),
        };

        foreach (var group in request.Lines.GroupBy(l => (l.SaleInvoiceLineId, l.Condition)))
        {
            var line = invoice.Lines.FirstOrDefault(l => l.Id == group.Key.SaleInvoiceLineId)
                ?? throw new ValidationFailedException("lines", "أحد البنود لا ينتمي لهذه الفاتورة");
            var quantity = group.Sum(g => g.Quantity);
            var returnable = line.Quantity - line.ReturnedQuantity;
            if (quantity > returnable)
                throw new BusinessRuleException($"الكمية المرتجعة من «{line.ProductName}» أكبر من المتبقي ({returnable:0.###})");

            line.ReturnedQuantity += quantity;
            // The last units returned take whatever net value remains, so a fully returned line reverses exactly.
            var value = line.ReturnedQuantity == line.Quantity
                ? line.NetTotal - CostMath.RoundMoney((line.Quantity - quantity) * line.NetTotal / line.Quantity)
                : CostMath.RoundMoney(quantity * line.NetTotal / line.Quantity);
            ret.Lines.Add(new SaleReturnLine
            {
                SaleInvoiceLineId = line.Id, ProductId = line.ProductId, Quantity = quantity, Condition = group.Key.Condition,
                LineTotal = value, UnitCost = line.UnitCost,
            });
        }
        if (ret.Lines.Count == 0) throw new ValidationFailedException("lines", "حدد المنتجات والكميات المرتجعة");

        ret.Total = ret.Lines.Sum(l => l.LineTotal);
        ret.TotalCost = ret.Lines.Sum(l => l.Quantity * l.UnitCost);
        invoice.Status = invoice.Lines.All(l => l.ReturnedQuantity == l.Quantity) ? SaleStatus.Returned : SaleStatus.PartiallyReturned;
        db.SaleReturns.Add(ret);
        await db.SaveChangesAsync(ct);

        // Sellable goods go back on the shelf at the cost they left with; damaged ones stay out (their cost is a loss).
        foreach (var line in ret.Lines.Where(l => l.Condition == ReturnCondition.Sellable).OrderBy(l => l.ProductId))
            await inventory.ReceiveAsync(new StockEntry(line.ProductId, invoice.WarehouseId, line.Quantity, StockMovementType.SaleReturn,
                "SaleReturn", ret.Id, ret.Number, $"مرتجع من {invoice.Number}"), line.UnitCost, ct);

        var source = new LedgerSource("SaleReturn", ret.Id, ret.Number, $"مرتجع من الفاتورة {invoice.Number}");
        if (ret.Total > 0)
        {
            if (!invoice.Customer.IsWalkIn)
                await customers.PostAsync(invoice.CustomerId, CustomerEntryType.Return, 0, ret.Total, source, now, ct);
            if (request.Settlement == ReturnSettlement.Refund)
            {
                await accounts.PostAsync(request.RefundAccountId!.Value, MoneyDirection.Out, ret.Total, AccountTransactionCategory.SaleRefund, source, now, ct);
                if (!invoice.Customer.IsWalkIn)
                    await customers.PostAsync(invoice.CustomerId, CustomerEntryType.Refund, ret.Total, 0, source with { Description = "إعادة المبلغ للعميل" }, now, ct);
            }
        }

        idempotency.Record(request.IdempotencyKey, ReturnOperation, "SaleReturn", ret.Id.ToString());
        audit.Log("Sale.Return", "SaleInvoice", id.ToString(), null, new { ret.Number, ret.Total, request.Settlement });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    // ------------------------------------------------------------------ void

    public async Task<SaleDetailDto> VoidAsync(int id, VoidRequest request, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var invoice = await db.SaleInvoices.Include(s => s.Lines).Include(s => s.Payments).Include(s => s.Customer)
            .FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("الفاتورة غير موجودة");
        if (invoice.Status == SaleStatus.Voided) throw new BusinessRuleException("الفاتورة ملغاة من قبل");
        if (invoice.Lines.Any(l => l.ReturnedQuantity > 0))
            throw new BusinessRuleException("لا يمكن إلغاء فاتورة عليها مرتجعات؛ أرجِع باقي الكميات بدلًا من الإلغاء");

        var now = clock.UtcNow;
        var reason = request.Reason.Trim();
        var source = new LedgerSource("SaleInvoice", invoice.Id, invoice.Number, $"إلغاء الفاتورة: {reason}");

        foreach (var line in invoice.Lines.OrderBy(l => l.ProductId))
            await inventory.ReceiveAsync(new StockEntry(line.ProductId, invoice.WarehouseId, line.Quantity, StockMovementType.SaleVoid,
                "SaleInvoice", invoice.Id, invoice.Number, "إلغاء فاتورة بيع"), line.UnitCost, ct);

        foreach (var payment in invoice.Payments)
            await accounts.PostAsync(payment.FinancialAccountId, MoneyDirection.Out, payment.Amount, AccountTransactionCategory.Reversal, source, now, ct);

        if (!invoice.Customer.IsWalkIn)
        {
            await customers.PostAsync(invoice.CustomerId, CustomerEntryType.Reversal, 0, invoice.Total, source, now, ct);
            if (invoice.PaidAmount > 0) await customers.PostAsync(invoice.CustomerId, CustomerEntryType.Reversal, invoice.PaidAmount, 0, source, now, ct);
        }

        invoice.Status = SaleStatus.Voided;
        invoice.VoidedAt = now;
        invoice.VoidedById = currentUser.UserId;
        invoice.VoidReason = reason;
        audit.Log("Sale.Void", "SaleInvoice", id.ToString(), null, new { invoice.Number, invoice.Total }, reason);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    // ------------------------------------------------------------------ summary

    public async Task<SalesSummaryDto> SummaryAsync(DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var start = from ?? clock.LocalToday;
        var end = to ?? start;
        var (startUtc, endUtc) = clock.LocalDaysToUtcRange(start, end);
        var ownOnly = !CanSeeAll;
        var me = currentUser.UserId;

        var sales = db.SaleInvoices.AsNoTracking().Where(s => s.Date >= startUtc && s.Date < endUtc && s.Status != SaleStatus.Voided);
        if (ownOnly) sales = sales.Where(s => s.SalespersonId == me);
        var totals = await sales.GroupBy(_ => 1).Select(g => new
        {
            Count = g.Count(),
            Gross = g.Sum(s => s.GrossTotal),
            Discounts = g.Sum(s => s.LineDiscountTotal + s.InvoiceDiscount),
            Net = g.Sum(s => s.Total),
            Cost = g.Sum(s => s.TotalCost),
            Paid = g.Sum(s => s.PaidAmount),
        }).FirstOrDefaultAsync(ct);

        // Returns are counted on the day they happen, whatever the date of the original invoice.
        var returns = db.SaleReturns.AsNoTracking().Where(r => r.Date >= startUtc && r.Date < endUtc);
        if (ownOnly) returns = returns.Where(r => r.SaleInvoice.SalespersonId == me);
        var returnTotals = await returns.GroupBy(_ => 1).Select(g => new
        {
            Total = g.Sum(r => r.Total),
            Cost = g.Sum(r => r.Lines.Where(l => l.Condition == ReturnCondition.Sellable).Sum(l => l.Quantity * l.UnitCost)),
        }).FirstOrDefaultAsync(ct);

        var received = db.AccountTransactions.AsNoTracking().Where(t => t.Date >= startUtc && t.Date < endUtc && t.Direction == MoneyDirection.In
            && (t.Category == AccountTransactionCategory.SaleReceipt || t.Category == AccountTransactionCategory.DebtCollection));
        if (ownOnly) received = received.Where(t => t.UserId == me);
        var sums = await received.GroupBy(t => t.FinancialAccountId)
            .Select(g => new { AccountId = g.Key, Amount = g.Sum(t => t.Amount) }).ToListAsync(ct);
        var sumIds = sums.Select(s => s.AccountId).ToList();
        var names = await db.FinancialAccounts.AsNoTracking().Where(a => sumIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Name, ct);
        var byAccount = sums.Select(s => new AccountAmountDto(s.AccountId, names[s.AccountId], s.Amount)).OrderByDescending(a => a.Amount).ToList();

        var gross = totals?.Gross ?? 0;
        var discounts = totals?.Discounts ?? 0;
        var returnsTotal = returnTotals?.Total ?? 0;
        var netSales = gross - discounts - returnsTotal;
        var cost = CostMath.RoundMoney((totals?.Cost ?? 0) - (returnTotals?.Cost ?? 0));

        return new SalesSummaryDto(start, end, ownOnly, totals?.Count ?? 0, gross, discounts, returnsTotal, netSales,
            CanSeeProfit ? cost : null, CanSeeProfit ? CostMath.RoundMoney(netSales - cost) : null,
            byAccount.Sum(a => a.Amount), (totals?.Net ?? 0) - (totals?.Paid ?? 0), byAccount);
    }
}
