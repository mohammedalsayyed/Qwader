using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Qwader.Api.Authorization;
using Qwader.Application.Finance;
using Qwader.Application.Payroll;
using Qwader.Domain.Identity;

namespace Qwader.Api.Controllers;

[ApiController]
[Route("api/payroll")]
[HasPermission(Permissions.PayrollManage)]
public sealed class PayrollController(IPayrollService payroll) : ControllerBase
{
    [HttpGet("employees")]
    public Task<IReadOnlyList<EmployeeListItemDto>> List(CancellationToken ct) => payroll.ListAsync(ct);

    /// <summary>Users not yet on the payroll.</summary>
    [HttpGet("candidates")]
    public Task<IReadOnlyList<PayrollCandidateDto>> Candidates(CancellationToken ct) => payroll.CandidatesAsync(ct);

    [HttpGet("employees/{id:int}")]
    public Task<EmployeeDto> Get(int id, CancellationToken ct) => payroll.GetAsync(id, ct);

    [HttpPost("employees")]
    public Task<EmployeeDto> Create(SaveEmployeeRequest request, CancellationToken ct) => payroll.CreateAsync(request, ct);

    [HttpPut("employees/{id:int}")]
    public Task<EmployeeDto> Update(int id, SaveEmployeeRequest request, CancellationToken ct) => payroll.UpdateAsync(id, request, ct);

    [HttpDelete("employees/{id:int}")]
    public async Task<IActionResult> Remove(int id, CancellationToken ct)
    {
        await payroll.RemoveAsync(id, ct);
        return NoContent();
    }

    /// <summary>An advance (paid from an account now), a deduction or a bonus for the next settlement.</summary>
    [HttpPost("employees/{id:int}/entries")]
    public Task<EmployeeDto> AddEntry(int id, CreatePayrollEntryRequest request, CancellationToken ct) => payroll.AddEntryAsync(id, request, ct);

    [HttpPost("entries/{id:int}/void")]
    public Task<EmployeeDto> VoidEntry(int id, VoidDocumentRequest request, CancellationToken ct) => payroll.VoidEntryAsync(id, request, ct);

    [HttpPost("employees/{id:int}/settlements")]
    public Task<EmployeeDto> Settle(int id, CreateSettlementRequest request, CancellationToken ct) => payroll.SettleAsync(id, request, ct);

    [HttpPost("settlements/{id:int}/void")]
    public Task<EmployeeDto> VoidSettlement(int id, VoidDocumentRequest request, CancellationToken ct) => payroll.VoidSettlementAsync(id, request, ct);
}

/// <summary>Every signed-in user sees their own salary record (if they have one), and nothing else.</summary>
[ApiController]
[Route("api/payroll/me")]
[Authorize]
public sealed class MyPayrollController(IPayrollService payroll) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<EmployeeDto>> Get(CancellationToken ct) => await payroll.MineAsync(ct) is { } mine ? mine : NoContent();
}
