using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Qwader.Application.Common;
using Qwader.Application.Finance;
using Qwader.Application.Purchasing;
using Qwader.Application.Sales;
using Qwader.Domain.Finance;
using Qwader.Domain.Sales;

namespace Qwader.IntegrationTests;

/// <summary>A payment voucher for an invoice (transport of a purchase, delivery of a sale): an expense tied to it, kept on an edit.</summary>
[Collection(ApiCollection.Name)]
public sealed class InvoiceExpenseTests(ApiFactory factory)
{
    [Fact]
    public async Task An_expense_is_tied_to_its_invoice_and_follows_it_when_the_invoice_is_edited()
    {
        var admin = await factory.LoginAsync();
        var cash = (await (await admin.PostAsJsonAsync("/api/financial-accounts",
            new SaveAccountRequest(TestData.Unique("صندوق مصاريف"), FinancialAccountType.Cash, null, null, null))).ReadAsync<AccountDto>()).Id;
        await admin.FundAsync(cash, 500);
        var category = (await admin.LookupsAsync()).ExpenseCategories.First().Id;
        var supplier = await admin.CreateSupplierAsync();
        var product = await admin.CreateProductAsync(retail: 50);
        var purchase = await admin.PurchaseAsync(supplier.Id, [new(product.Id, 10, 20)]);

        // Transport of the purchase.
        var transport = await (await admin.PostAsJsonAsync("/api/expenses", new CreateExpenseRequest(Guid.NewGuid(), category, "مواصلات البضاعة", 40, cash,
            null, null, PurchaseInvoiceId: purchase.Id))).ReadAsync<ExpenseDto>();
        transport.PurchaseInvoiceNumber.Should().Be(purchase.Number);
        var voucher = await (await admin.GetAsync($"/api/payment-vouchers/{transport.PaymentVoucherId}")).ReadAsync<VoucherDto>();
        voucher.Notes.Should().Contain(purchase.Number);
        (await (await admin.GetAsync($"/api/financial-accounts/{cash}")).ReadAsync<AccountDto>()).CurrentBalance.Should().Be(460);

        // Delivery of a sale.
        var sale = await (await admin.PostAsJsonAsync("/api/sales", new CreateSaleRequest(Guid.NewGuid(), PriceTier.Retail, null, null, null, 0,
            [new(product.Id, 1, 50)], [new(cash, 50)], null))).ReadAsync<SaleDetailDto>();
        (await admin.PostAsJsonAsync("/api/expenses", new CreateExpenseRequest(Guid.NewGuid(), category, "توصيل الغسالة", 15, cash,
            null, null, SaleInvoiceId: sale.Id))).EnsureSuccessStatusCode();
        async Task<PagedResult<ExpenseDto>> Of(string filter) => await (await admin.GetAsync($"/api/expenses?{filter}")).ReadAsync<PagedResult<ExpenseDto>>();
        (await Of($"saleInvoiceId={sale.Id}")).Items.Should().ContainSingle().Which.Amount.Should().Be(15);

        // Editing the purchase (void + new): the transport moves to the new invoice.
        var edited = await (await admin.PostAsJsonAsync($"/api/purchases/{purchase.Id}/replace", new SavePurchaseRequest(Guid.NewGuid(), supplier.Id, null, null, null,
            0, 0, null, null, [new(product.Id, 12, 20)], true))).ReadAsync<PurchaseDetailDto>();
        (await Of($"purchaseInvoiceId={edited.Id}")).Items.Should().ContainSingle().Which.Id.Should().Be(transport.Id);
        (await Of($"purchaseInvoiceId={purchase.Id}")).Items.Should().BeEmpty();

        // Not for two invoices at once, nor for a voided one.
        (await admin.PostAsJsonAsync("/api/expenses", new CreateExpenseRequest(Guid.NewGuid(), category, "x", 1, cash, null, null, edited.Id, sale.Id)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync("/api/expenses", new CreateExpenseRequest(Guid.NewGuid(), category, "x", 1, cash, null, null, PurchaseInvoiceId: purchase.Id)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
