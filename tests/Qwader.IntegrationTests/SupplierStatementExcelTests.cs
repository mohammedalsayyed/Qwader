using System.Net.Http.Json;
using ClosedXML.Excel;
using FluentAssertions;
using Qwader.Application.Finance;
using Qwader.Application.Purchasing;
using Qwader.Domain.Finance;
using Qwader.Domain.Purchasing;

namespace Qwader.IntegrationTests;

/// <summary>The supplier's statement as Excel: what was bought, paid and returned, what is still owed, each invoice in its section.</summary>
[Collection(ApiCollection.Name)]
public sealed class SupplierStatementExcelTests(ApiFactory factory)
{
    [Fact]
    public async Task The_workbook_adds_up_to_the_supplier_balance()
    {
        var admin = await factory.LoginAsync();
        var cash = (await admin.LookupsAsync()).Accounts.First(a => a.Type == FinancialAccountType.Cash).Id;
        await admin.FundAsync(cash, 1000);
        var supplier = await admin.CreateSupplierAsync(openingBalance: 100);
        var a = await admin.CreateProductAsync(retail: 50);
        var b = await admin.CreateProductAsync(retail: 30);

        var first = await admin.PurchaseAsync(supplier.Id, [new(a.Id, 4, 25), new(b.Id, 10, 12)], paid: 50, accountId: cash);  // 220, 50 paid
        var voided = await admin.PurchaseAsync(supplier.Id, [new(a.Id, 1, 25)]);
        (await admin.PostAsJsonAsync($"/api/purchases/{voided.Id}/void", new { reason = "خطأ" })).EnsureSuccessStatusCode();
        await (await admin.PostAsJsonAsync($"/api/purchases/{first.Id}/returns", new PurchaseReturnRequest(Guid.NewGuid(), null,
            ReturnSettlement.ReduceBalance, null, "تالف", [new ReturnLineInput(first.Lines[1].Id, 2)]))).ReadAsync<PurchaseDetailDto>();       // −24
        (await admin.PostAsJsonAsync("/api/payment-vouchers", new CreatePaymentRequest(Guid.NewGuid(), PaymentPurpose.SupplierPayment, supplier.Id, null,
            30, cash, "شيك 5", null, null))).EnsureSuccessStatusCode();

        var balance = (await (await admin.GetAsync($"/api/suppliers/{supplier.Id}")).ReadAsync<SupplierDto>()).Balance;
        balance.Should().Be(100 + 220 - 50 - 24 - 30);

        var response = await admin.GetAsync($"/api/suppliers/{supplier.Id}/statement/export");
        response.EnsureSuccessStatusCode();
        using var wb = new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
        wb.Worksheets.Select(w => w.Name).Should().Equal("الملخص", "فواتير الشراء", "كشف الحساب", "الدفعات");

        var summary = wb.Worksheet("الملخص");
        decimal Value(string label) => summary.RowsUsed().Single(r => r.Cell(1).GetString() == label).Cell(2).GetValue<decimal>();
        Value("المشتريات (فواتير الشراء)").Should().Be(220, "the voided invoice does not count");
        Value("المدفوع للمورد").Should().Be(-80);
        Value("المردودات للمورد").Should().Be(-24);
        Value("رصيد سابق (قبل النظام)").Should().Be(100);
        Value("الباقي له علينا").Should().Be(balance);

        var invoices = wb.Worksheet("فواتير الشراء");
        var titles = invoices.RowsUsed().Select(r => r.Cell(1).GetString()).Where(t => t.StartsWith("فاتورة ")).ToList();
        titles.Should().HaveCount(2);
        titles.Should().Contain(t => t.Contains(first.Number) && !t.Contains("ملغاة"));
        titles.Should().Contain(t => t.Contains(voided.Number) && t.Contains("ملغاة"));
        invoices.RowsUsed().Should().Contain(r => r.Cell(1).GetString().StartsWith(a.Name) && r.Cell(2).GetValue<decimal>() == 4
            && r.Cell(3).GetValue<decimal>() == 25 && r.Cell(4).GetValue<decimal>() == 100, "صنف | عدد | سعر | إجمالي");
        // The list at the top: every invoice of the period, the voided one marked and left out of the total.
        invoices.RowsUsed().Should().Contain(r => r.Cell(1).GetString() == first.Number && r.Cell(4).GetValue<decimal>() == 220);
        invoices.RowsUsed().Should().Contain(r => r.Cell(1).GetString() == $"{voided.Number} (ملغاة)");
        invoices.RowsUsed().First(r => r.Cell(1).GetString().StartsWith("المجموع (")).Cell(4).GetValue<decimal>().Should().Be(220);

        var payments = wb.Worksheet("الدفعات");
        payments.RowsUsed().Single(r => r.Cell(1).GetString() == "مجموع الدفعات").Cell(5).GetValue<decimal>().Should().Be(80);
    }
}
