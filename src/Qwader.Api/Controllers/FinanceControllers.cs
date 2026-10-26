using Microsoft.AspNetCore.Mvc;
using Qwader.Api.Authorization;
using Qwader.Application.Common;
using Qwader.Application.Finance;
using Qwader.Domain.Identity;

namespace Qwader.Api.Controllers;

[ApiController]
[Route("api/financial-accounts")]
[HasPermission(Permissions.AccountsView, Permissions.AccountsManage)]
public sealed class FinancialAccountsController(IAccountService accounts) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<AccountDto>> List([FromQuery] bool includeInactive, CancellationToken ct) => accounts.ListAsync(includeInactive, ct);

    [HttpGet("{id:int}")]
    public Task<AccountDto> Get(int id, CancellationToken ct) => accounts.GetAsync(id, ct);

    [HttpGet("{id:int}/statement")]
    public Task<AccountStatementDto> Statement(int id, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] PagedRequest paging, CancellationToken ct) =>
        accounts.StatementAsync(id, from, to, paging, ct);

    [HttpPost]
    [HasPermission(Permissions.AccountsManage)]
    public Task<AccountDto> Create(SaveAccountRequest request, CancellationToken ct) => accounts.CreateAsync(request, ct);

    [HttpPut("{id:int}")]
    [HasPermission(Permissions.AccountsManage)]
    public Task<AccountDto> Update(int id, SaveAccountRequest request, CancellationToken ct) => accounts.UpdateAsync(id, request, ct);

    [HttpPost("{id:int}/opening-balance")]
    [HasPermission(Permissions.AccountsManage)]
    public Task<AccountDto> SetOpening(int id, SetOpeningBalanceRequest request, CancellationToken ct) => accounts.SetOpeningBalanceAsync(id, request, ct);
}

[ApiController]
[Route("api/account-transfers")]
[HasPermission(Permissions.AccountsManage)]
public sealed class AccountTransfersController(IAccountService accounts) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<AccountTransferDto>> List([FromQuery] PagedRequest request, CancellationToken ct) => accounts.TransfersAsync(request, ct);

    [HttpPost]
    public Task<AccountTransferDto> Create(CreateAccountTransferRequest request, CancellationToken ct) => accounts.TransferAsync(request, ct);
}

[ApiController]
[Route("api/incoming-transfers")]
[HasPermission(Permissions.TransfersCreate, Permissions.TransfersReview)]
public sealed class IncomingTransfersController(ITransferService transfers) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<IncomingTransferDto>> List([FromQuery] TransferListRequest request, CancellationToken ct) => transfers.ListAsync(request, ct);

    [HttpGet("{id:int}")]
    public Task<IncomingTransferDto> Get(int id, CancellationToken ct) => transfers.GetAsync(id, ct);

    [HttpPost]
    [HasPermission(Permissions.TransfersCreate)]
    public Task<IncomingTransferDto> Create(CreateIncomingTransferRequest request, CancellationToken ct) => transfers.CreateAsync(request, ct);

    [HttpPost("{id:int}/confirm")]
    [HasPermission(Permissions.TransfersReview)]
    public Task<IncomingTransferDto> Confirm(int id, [FromQuery] bool acceptCredit, CancellationToken ct) => transfers.ConfirmAsync(id, acceptCredit, ct);

    [HttpPost("{id:int}/reject")]
    [HasPermission(Permissions.TransfersReview)]
    public Task<IncomingTransferDto> Reject(int id, RejectTransferRequest request, CancellationToken ct) => transfers.RejectAsync(id, request, ct);
}

[ApiController]
[Route("api/receipt-vouchers")]
[HasPermission(Permissions.VouchersCreate, Permissions.ReportsFinancial)]
public sealed class ReceiptVouchersController(IVoucherService vouchers) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<VoucherDto>> List([FromQuery] VoucherListRequest request, CancellationToken ct) => vouchers.ReceiptsAsync(request, ct);

    [HttpGet("{id:int}")]
    public Task<VoucherDto> Get(int id, CancellationToken ct) => vouchers.GetReceiptAsync(id, ct);

    [HttpPost]
    [HasPermission(Permissions.VouchersCreate)]
    public Task<VoucherDto> Create(CreateReceiptRequest request, CancellationToken ct) => vouchers.CreateReceiptAsync(request, ct);

    [HttpPost("{id:int}/void")]
    [HasPermission(Permissions.VouchersVoid)]
    public Task<VoucherDto> Void(int id, VoidDocumentRequest request, CancellationToken ct) => vouchers.VoidReceiptAsync(id, request, ct);
}

[ApiController]
[Route("api/payment-vouchers")]
[HasPermission(Permissions.VouchersCreate, Permissions.ReportsFinancial)]
public sealed class PaymentVouchersController(IVoucherService vouchers) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<VoucherDto>> List([FromQuery] VoucherListRequest request, CancellationToken ct) => vouchers.PaymentsAsync(request, ct);

    [HttpGet("{id:int}")]
    public Task<VoucherDto> Get(int id, CancellationToken ct) => vouchers.GetPaymentAsync(id, ct);

    [HttpPost]
    [HasPermission(Permissions.VouchersCreate)]
    public Task<VoucherDto> Create(CreatePaymentRequest request, CancellationToken ct) => vouchers.CreatePaymentAsync(request, ct);

    [HttpPost("{id:int}/void")]
    [HasPermission(Permissions.VouchersVoid)]
    public Task<VoucherDto> Void(int id, VoidDocumentRequest request, CancellationToken ct) => vouchers.VoidPaymentAsync(id, request, ct);
}

[ApiController]
[Route("api/expenses")]
[HasPermission(Permissions.ExpensesManage)]
public sealed class ExpensesController(IVoucherService vouchers) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<ExpenseDto>> List([FromQuery] VoucherListRequest request, CancellationToken ct) => vouchers.ExpensesAsync(request, ct);

    [HttpPost]
    public Task<ExpenseDto> Create(CreateExpenseRequest request, CancellationToken ct) => vouchers.CreateExpenseAsync(request, ct);

    [HttpPost("{id:int}/void")]
    [HasPermission(Permissions.VouchersVoid, Permissions.ExpensesManage)]
    public Task<ExpenseDto> Void(int id, VoidDocumentRequest request, CancellationToken ct) => vouchers.VoidExpenseAsync(id, request, ct);

    [HttpGet("categories")]
    public Task<IReadOnlyList<ExpenseCategoryDto>> Categories(CancellationToken ct) => vouchers.CategoriesAsync(ct);

    [HttpPost("categories")]
    public Task<ExpenseCategoryDto> CreateCategory(SaveExpenseCategoryRequest request, CancellationToken ct) => vouchers.SaveCategoryAsync(null, request, ct);

    [HttpPut("categories/{id:int}")]
    public Task<ExpenseCategoryDto> UpdateCategory(int id, SaveExpenseCategoryRequest request, CancellationToken ct) => vouchers.SaveCategoryAsync(id, request, ct);
}
