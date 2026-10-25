using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Qwader.Application.Auth;
using Qwader.Application.Customers;
using Qwader.Application.Finance;
using Qwader.Application.Purchasing;
using Qwader.Application.Sales;
using Qwader.Application.Users;
using Qwader.Domain.Finance;
using Qwader.Domain.Identity;
using Qwader.Domain.Sales;

namespace Qwader.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class FinanceTests(ApiFactory factory)
{
    private static string Phone() => "056" + Random.Shared.Next(1000000, 9999999);

    private async Task<AccountDto> NewAccountAsync(HttpClient admin, FinancialAccountType type = FinancialAccountType.Wallet) =>
        await (await admin.PostAsJsonAsync("/api/financial-accounts", new SaveAccountRequest(TestData.Unique("حساب"), type, "مزود", null, "1234567890")))
            .ReadAsync<AccountDto>();

    private static async Task<decimal> BalanceAsync(HttpClient c, int id) =>
        (await (await c.GetAsync($"/api/financial-accounts/{id}")).ReadAsync<AccountDto>()).CurrentBalance;

    private async Task<SaleDetailDto> CreditSaleAsync(HttpClient admin, decimal total, string phone)
    {
        var product = await admin.CreateProductAsync(retail: total, openingQuantity: 5, openingCost: 1);
        return await (await admin.PostAsJsonAsync("/api/sales", new CreateSaleRequest(Guid.NewGuid(), PriceTier.Retail, null, "عميل مالي", phone, 0,
            [new(product.Id, 1, total)], [], null))).ReadAsync<SaleDetailDto>();
    }

    [Fact]
    public async Task Opening_balance_is_posted_as_a_movement_and_the_balance_always_equals_the_movements()
    {
        var admin = await factory.LoginAsync();
        var account = await NewAccountAsync(admin);

        await (await admin.PostAsJsonAsync($"/api/financial-accounts/{account.Id}/opening-balance", new SetOpeningBalanceRequest(500, "رصيد بداية"))).ReadAsync<AccountDto>();
        var corrected = await (await admin.PostAsJsonAsync($"/api/financial-accounts/{account.Id}/opening-balance", new SetOpeningBalanceRequest(450, "تصحيح"))).ReadAsync<AccountDto>();

        corrected.CurrentBalance.Should().Be(450);
        var statement = await (await admin.GetAsync($"/api/financial-accounts/{account.Id}/statement")).ReadAsync<AccountStatementDto>();
        statement.Movements.Items.Should().HaveCount(2).And.OnlyContain(m => m.Category == AccountTransactionCategory.OpeningBalance);
        (statement.TotalIn - statement.TotalOut).Should().Be(450);
    }

    [Fact]
    public async Task Account_numbers_are_masked_without_permission_and_kept_on_update()
    {
        var admin = await factory.LoginAsync();
        var account = await NewAccountAsync(admin);
        account.AccountNumber.Should().Be("1234567890", "admin may view numbers");
        var userName = TestData.Unique("acct");
        var roles = await admin.GetFromJsonAsync<List<RoleDto>>("/api/roles", TestJson.Options);
        var role = await (await admin.PostAsJsonAsync("/api/roles", new SaveRoleRequest(TestData.Unique("دور"), null, [Permissions.AccountsView, Permissions.AccountsManage]))).ReadAsync<RoleDto>();
        (await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(userName, "محاسب", null, "Temp12345", [role.Name]))).EnsureSuccessStatusCode();
        var temp = await factory.LoginAsync(userName, "Temp12345");
        (await temp.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("Temp12345", "Acct12345"))).EnsureSuccessStatusCode();
        var user = await factory.LoginAsync(userName, "Acct12345");

        var seen = await (await user.GetAsync($"/api/financial-accounts/{account.Id}")).ReadAsync<AccountDto>();
        seen.AccountNumber.Should().Be("•••• 7890");
        await (await user.PutAsJsonAsync($"/api/financial-accounts/{account.Id}", new SaveAccountRequest("اسم جديد " + account.Name, seen.Type, null, null, seen.AccountNumber))).ReadAsync<AccountDto>();
        (await (await admin.GetAsync($"/api/financial-accounts/{account.Id}")).ReadAsync<AccountDto>()).AccountNumber.Should().Be("1234567890");
        roles.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Transfer_between_accounts_moves_money_and_refuses_overdraft()
    {
        var admin = await factory.LoginAsync();
        var from = await NewAccountAsync(admin);
        var to = await NewAccountAsync(admin);
        await admin.FundAsync(from.Id, 300);

        await (await admin.PostAsJsonAsync("/api/account-transfers", new CreateAccountTransferRequest(Guid.NewGuid(), from.Id, to.Id, 120, "إيداع"))).ReadAsync<AccountTransferDto>();
        (await BalanceAsync(admin, from.Id)).Should().Be(180);
        (await BalanceAsync(admin, to.Id)).Should().Be(120);

        var overdraft = await admin.PostAsJsonAsync("/api/account-transfers", new CreateAccountTransferRequest(Guid.NewGuid(), from.Id, to.Id, 500, null));
        overdraft.StatusCode.Should().Be((HttpStatusCode)422);
        (await BalanceAsync(admin, from.Id)).Should().Be(180);
    }

    [Fact]
    public async Task Pending_transfer_is_not_money_until_confirmed_and_then_reduces_the_debt()
    {
        var admin = await factory.LoginAsync();
        var account = await NewAccountAsync(admin);
        var sale = await CreditSaleAsync(admin, 200, Phone());
        var reference = TestData.Unique("REF");

        var transfer = await (await admin.PostAsJsonAsync("/api/incoming-transfers", new CreateIncomingTransferRequest(Guid.NewGuid(), "عميل مالي", null, 150,
            "بنك فلسطين", account.Id, reference, null, null, sale.CustomerId, null))).ReadAsync<IncomingTransferDto>();
        transfer.Status.Should().Be(TransferStatus.Pending);
        (await BalanceAsync(admin, account.Id)).Should().Be(0, "an unconfirmed transfer is not money received");

        var duplicate = await admin.PostAsJsonAsync("/api/incoming-transfers", new CreateIncomingTransferRequest(Guid.NewGuid(), "آخر", null, 150,
            null, account.Id, reference, null, null, null, null));
        duplicate.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var confirmed = await (await admin.PostAsync($"/api/incoming-transfers/{transfer.Id}/confirm", null)).ReadAsync<IncomingTransferDto>();
        confirmed.Status.Should().Be(TransferStatus.Confirmed);
        confirmed.ReceiptVoucherNumber.Should().StartWith("RV-");
        (await BalanceAsync(admin, account.Id)).Should().Be(150);
        (await admin.GetFromJsonAsync<CustomerDto>($"/api/customers/{sale.CustomerId}", TestJson.Options))!.Balance.Should().Be(50);

        (await admin.PostAsync($"/api/incoming-transfers/{transfer.Id}/confirm", null)).StatusCode.Should().Be((HttpStatusCode)422, "a transfer counts once");
    }

    [Fact]
    public async Task Rejected_transfer_changes_nothing()
    {
        var admin = await factory.LoginAsync();
        var account = await NewAccountAsync(admin);
        var transfer = await (await admin.PostAsJsonAsync("/api/incoming-transfers", new CreateIncomingTransferRequest(Guid.NewGuid(), "مرسل", Phone(), 80,
            null, account.Id, TestData.Unique("R"), null, null, null, null))).ReadAsync<IncomingTransferDto>();

        var rejected = await (await admin.PostAsJsonAsync($"/api/incoming-transfers/{transfer.Id}/reject", new RejectTransferRequest("لم تصل الحوالة"))).ReadAsync<IncomingTransferDto>();

        rejected.Status.Should().Be(TransferStatus.Rejected);
        (await BalanceAsync(admin, account.Id)).Should().Be(0);
    }

    [Fact]
    public async Task Till_transfer_awaiting_verification_stays_as_debt_until_confirmed()
    {
        var admin = await factory.LoginAsync();
        var account = await NewAccountAsync(admin);
        var product = await admin.CreateProductAsync(retail: 100, openingQuantity: 3, openingCost: 10);

        var walkIn = await admin.PostAsJsonAsync("/api/sales", new CreateSaleRequest(Guid.NewGuid(), PriceTier.Retail, null, null, null, 0,
            [new(product.Id, 1, 100)], [new(account.Id, 100, "X1", Pending: true)], null));
        walkIn.StatusCode.Should().Be((HttpStatusCode)422, "an unverified transfer needs a named customer");

        var sale = await (await admin.PostAsJsonAsync("/api/sales", new CreateSaleRequest(Guid.NewGuid(), PriceTier.Retail, null, "زبون حوالة", Phone(), 0,
            [new(product.Id, 1, 100)], [new(account.Id, 100, TestData.Unique("TX"), Pending: true)], null))).ReadAsync<SaleDetailDto>();

        sale.PaidAmount.Should().Be(0);
        sale.Transfers.Should().ContainSingle(t => t.Status == TransferStatus.Pending);
        (await admin.GetFromJsonAsync<CustomerDto>($"/api/customers/{sale.CustomerId}", TestJson.Options))!.Balance.Should().Be(100);

        await (await admin.PostAsync($"/api/incoming-transfers/{sale.Transfers[0].Id}/confirm", null)).ReadAsync<IncomingTransferDto>();
        (await admin.GetFromJsonAsync<CustomerDto>($"/api/customers/{sale.CustomerId}", TestJson.Options))!.Balance.Should().Be(0);
        (await BalanceAsync(admin, account.Id)).Should().Be(100);
    }

    [Fact]
    public async Task Receipt_voucher_collects_debt_without_counting_as_a_sale_and_guards_against_doubles()
    {
        var admin = await factory.LoginAsync();
        var account = await NewAccountAsync(admin, FinancialAccountType.Cash);
        var sale = await CreditSaleAsync(admin, 300, Phone());
        var request = new CreateReceiptRequest(Guid.NewGuid(), ReceiptPurpose.DebtCollection, sale.CustomerId, null, null, 100, account.Id, null, "دفعة أولى", null);

        var receipt = await (await admin.PostAsJsonAsync("/api/receipt-vouchers", request)).ReadAsync<VoucherDto>();
        receipt.Number.Should().MatchRegex(@"^RV-\d{4}-\d{6}$");
        (await admin.GetFromJsonAsync<CustomerDto>($"/api/customers/{sale.CustomerId}", TestJson.Options))!.Balance.Should().Be(200);
        (await BalanceAsync(admin, account.Id)).Should().Be(100);

        var again = await admin.PostAsJsonAsync("/api/receipt-vouchers", request with { IdempotencyKey = Guid.NewGuid() });
        again.StatusCode.Should().Be(HttpStatusCode.Conflict, "same payer, amount and account minutes apart needs confirmation");
        (await admin.PostAsJsonAsync("/api/receipt-vouchers", request with { IdempotencyKey = Guid.NewGuid(), ConfirmDuplicate = true })).EnsureSuccessStatusCode();
        (await admin.GetFromJsonAsync<CustomerDto>($"/api/customers/{sale.CustomerId}", TestJson.Options))!.Balance.Should().Be(100);

        var tooMuch = await admin.PostAsJsonAsync("/api/receipt-vouchers", request with { IdempotencyKey = Guid.NewGuid(), Amount = 150, ConfirmDuplicate = true });
        tooMuch.StatusCode.Should().Be((HttpStatusCode)422);

        await (await admin.PostAsJsonAsync($"/api/receipt-vouchers/{receipt.Id}/void", new VoidDocumentRequest("سند مكرر"))).ReadAsync<VoucherDto>();
        (await admin.GetFromJsonAsync<CustomerDto>($"/api/customers/{sale.CustomerId}", TestJson.Options))!.Balance.Should().Be(200);
        (await BalanceAsync(admin, account.Id)).Should().Be(100);
    }

    [Fact]
    public async Task Payment_voucher_settles_supplier_debt_and_needs_funds()
    {
        var admin = await factory.LoginAsync();
        var account = await NewAccountAsync(admin, FinancialAccountType.Bank);
        var supplier = await admin.CreateSupplierAsync(openingBalance: 400);

        var noFunds = await admin.PostAsJsonAsync("/api/payment-vouchers", new CreatePaymentRequest(Guid.NewGuid(), PaymentPurpose.SupplierPayment, supplier.Id, null,
            100, account.Id, null, null, null));
        noFunds.StatusCode.Should().Be((HttpStatusCode)422);

        await admin.FundAsync(account.Id, 1000);
        await (await admin.PostAsJsonAsync("/api/payment-vouchers", new CreatePaymentRequest(Guid.NewGuid(), PaymentPurpose.SupplierPayment, supplier.Id, null,
            250, account.Id, "CHK-9", null, null))).ReadAsync<VoucherDto>();

        (await admin.GetFromJsonAsync<SupplierDto>($"/api/suppliers/{supplier.Id}", TestJson.Options))!.Balance.Should().Be(150);
        (await BalanceAsync(admin, account.Id)).Should().Be(750);
    }

    [Fact]
    public async Task Expense_is_paid_by_one_voucher_and_voiding_it_reverses_the_money()
    {
        var admin = await factory.LoginAsync();
        var account = await NewAccountAsync(admin, FinancialAccountType.Cash);
        await admin.FundAsync(account.Id, 500);
        var category = (await admin.LookupsAsync()).ExpenseCategories.First();

        var expense = await (await admin.PostAsJsonAsync("/api/expenses", new CreateExpenseRequest(Guid.NewGuid(), category.Id, "فاتورة كهرباء الشهر", 120, account.Id, null, null)))
            .ReadAsync<ExpenseDto>();
        expense.PaymentVoucherNumber.Should().StartWith("PV-");
        (await BalanceAsync(admin, account.Id)).Should().Be(380);

        (await admin.PostAsJsonAsync($"/api/payment-vouchers/{expense.PaymentVoucherId}/void", new VoidDocumentRequest("محاولة")))
            .StatusCode.Should().Be((HttpStatusCode)422, "an expense voucher is voided through its expense");

        await (await admin.PostAsJsonAsync($"/api/expenses/{expense.Id}/void", new VoidDocumentRequest("مسجل بالخطأ"))).ReadAsync<ExpenseDto>();
        (await BalanceAsync(admin, account.Id)).Should().Be(500);
    }

    [Fact]
    public async Task Only_roles_with_the_wholesale_permission_can_sell_at_wholesale()
    {
        var admin = await factory.LoginAsync();
        var product = await admin.CreateProductAsync(retail: 30, openingQuantity: 5, openingCost: 5);
        var userName = TestData.Unique("whs");
        (await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(userName, "بائع", null, "Temp12345", [SystemRoles.Sales]))).EnsureSuccessStatusCode();
        var temp = await factory.LoginAsync(userName, "Temp12345");
        (await temp.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("Temp12345", "Seller12345"))).EnsureSuccessStatusCode();
        var seller = await factory.LoginAsync(userName, "Seller12345");
        var cash = (await seller.LookupsAsync()).Accounts.First(a => a.Name == "الصندوق النقدي").Id;

        var response = await seller.PostAsJsonAsync("/api/sales", new CreateSaleRequest(Guid.NewGuid(), PriceTier.Wholesale, null, null, null, 0,
            [new(product.Id, 1, 30)], [new(cash, 30)], null));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var me = await seller.GetFromJsonAsync<CurrentUserDto>("/api/auth/me", TestJson.Options);
        me!.Permissions.Should().NotContain(Permissions.SalesWholesale);
        (await admin.GetFromJsonAsync<CurrentUserDto>("/api/auth/me", TestJson.Options))!.Permissions.Should().Contain(Permissions.SalesWholesale);
    }
}
