using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Qwader.Application.Abstractions;
using Qwader.Application.Common;
using Qwader.Application.Finance;
using Qwader.Application.Notifications;
using Qwader.Domain.Administration;
using Qwader.Domain.Finance;
using Qwader.Domain.Inventory;
using Qwader.Domain.Payroll;

namespace Qwader.Application.Payroll;

public sealed record EmployeeListItemDto(
    int Id, int UserId, string FullName, string? Phone, PayPeriod PayPeriod, decimal Rate, bool IsActive,
    decimal OpenAdvances, decimal OpenDeductions, decimal OpenBonuses, DateOnly? LastPaidTo);

public sealed record PayrollEntryDto(
    int Id, PayrollEntryType Type, DateTime Date, decimal Amount, string? Notes, string? AccountName, string? ExpenseNumber,
    bool Carried, int? SettlementId, string? SettlementNumber, VoucherStatus Status, string? VoidReason, string? CreatedByName);

public sealed record PayrollSettlementDto(
    int Id, string Number, DateTime Date, DateOnly PeriodFrom, DateOnly PeriodTo, PayPeriod PayPeriod, decimal Units, decimal Rate,
    decimal Gross, decimal Bonuses, decimal Deductions, decimal Advances, decimal Net, decimal CarriedAdvance,
    string? AccountName, string? ExpenseNumber, string? Notes, VoucherStatus Status, string? VoidReason, string? CreatedByName);

public sealed record EmployeeDto(
    int Id, int UserId, string FullName, string UserName, string? Phone, PayPeriod PayPeriod, decimal Rate, DateOnly? StartDate,
    string? Notes, bool IsActive, decimal OpenAdvances, decimal OpenDeductions, decimal OpenBonuses, DateOnly? LastPaidTo,
    IReadOnlyList<PayrollEntryDto> Entries, IReadOnlyList<PayrollSettlementDto> Settlements);

public sealed record PayrollCandidateDto(int UserId, string FullName, string UserName, string? Phone);

public sealed record SaveEmployeeRequest(int UserId, string? Phone, PayPeriod PayPeriod, decimal Rate, DateOnly? StartDate, string? Notes, bool IsActive = true);

public sealed record CreatePayrollEntryRequest(
    Guid IdempotencyKey, PayrollEntryType Type, decimal Amount, DateOnly? Date, int? FinancialAccountId, string? Notes);

public sealed record CreateSettlementRequest(
    Guid IdempotencyKey, DateOnly PeriodFrom, DateOnly PeriodTo, decimal Units, int? FinancialAccountId, string? Notes);

public interface IPayrollService
{
    Task<IReadOnlyList<EmployeeListItemDto>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PayrollCandidateDto>> CandidatesAsync(CancellationToken ct = default);
    Task<EmployeeDto> GetAsync(int id, CancellationToken ct = default);
    /// <summary>The signed-in user's own salary record, or null when they are not an employee.</summary>
    Task<EmployeeDto?> MineAsync(CancellationToken ct = default);
    Task<EmployeeDto> CreateAsync(SaveEmployeeRequest request, CancellationToken ct = default);
    Task<EmployeeDto> UpdateAsync(int id, SaveEmployeeRequest request, CancellationToken ct = default);
    /// <summary>Takes someone off the payroll who has no advances, deductions, bonuses or settlements (added by mistake).</summary>
    Task RemoveAsync(int id, CancellationToken ct = default);
    Task<EmployeeDto> AddEntryAsync(int employeeId, CreatePayrollEntryRequest request, CancellationToken ct = default);
    Task<EmployeeDto> VoidEntryAsync(int entryId, VoidDocumentRequest request, CancellationToken ct = default);
    Task<EmployeeDto> SettleAsync(int employeeId, CreateSettlementRequest request, CancellationToken ct = default);
    Task<EmployeeDto> VoidSettlementAsync(int settlementId, VoidDocumentRequest request, CancellationToken ct = default);
}

public sealed class SaveEmployeeRequestValidator : AbstractValidator<SaveEmployeeRequest>
{
    public SaveEmployeeRequestValidator()
    {
        RuleFor(x => x.UserId).GreaterThan(0).WithMessage("اختر المستخدم");
        RuleFor(x => x.Phone).MaximumLength(20).Must(Customers.PhoneNumbers.IsValid).WithMessage(Customers.PhoneNumbers.Invalid);
        RuleFor(x => x.PayPeriod).IsInEnum().WithMessage("اختر نوع الراتب");
        RuleFor(x => x.Rate).GreaterThanOrEqualTo(0).WithMessage("الراتب لا يمكن أن يكون سالبًا").LessThan(10_000_000);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

public sealed class CreatePayrollEntryRequestValidator : AbstractValidator<CreatePayrollEntryRequest>
{
    public CreatePayrollEntryRequestValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("المبلغ يجب أن يكون أكبر من صفر").LessThan(10_000_000);
        RuleFor(x => x.FinancialAccountId).NotNull().When(x => x.Type == PayrollEntryType.Advance).WithMessage("اختر الحساب الذي تُصرف منه السلفة");
        RuleFor(x => x.Notes).MaximumLength(300);
    }
}

public sealed class CreateSettlementRequestValidator : AbstractValidator<CreateSettlementRequest>
{
    public CreateSettlementRequestValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.PeriodTo).GreaterThanOrEqualTo(x => x.PeriodFrom).WithMessage("نهاية الفترة قبل بدايتها");
        RuleFor(x => x.Units).GreaterThan(0).WithMessage("أدخل عدد الأيام أو الأسابيع أو الأشهر").LessThanOrEqualTo(400);
        RuleFor(x => x.Notes).MaximumLength(300);
    }
}

public sealed class PayrollService(
    IAppDbContext db,
    IVoucherService vouchers,
    IDocumentNumberGenerator numbers,
    IIdempotencyService idempotency,
    ICurrentUser currentUser,
    IClock clock,
    IAuditService audit,
    IPushNotifier push) : IPayrollService
{
    private const string SalaryCategory = "الرواتب";

    public static string PeriodName(PayPeriod p) => p switch { PayPeriod.Daily => "يومي", PayPeriod.Weekly => "أسبوعي", _ => "شهري" };

    // ------------------------------------------------------------------ read

    public async Task<IReadOnlyList<EmployeeListItemDto>> ListAsync(CancellationToken ct = default)
    {
        var open = db.PayrollEntries.Where(e => e.SettlementId == null && e.Status == VoucherStatus.Posted);
        return await db.Employees.AsNoTracking()
            .OrderByDescending(e => e.IsActive).ThenBy(e => e.User.FullName)
            .Select(e => new EmployeeListItemDto(e.Id, e.UserId, e.User.FullName, e.User.PhoneNumber, e.PayPeriod, e.Rate, e.IsActive,
                open.Where(x => x.EmployeeId == e.Id && x.Type == PayrollEntryType.Advance).Sum(x => (decimal?)x.Amount) ?? 0,
                open.Where(x => x.EmployeeId == e.Id && x.Type == PayrollEntryType.Deduction).Sum(x => (decimal?)x.Amount) ?? 0,
                open.Where(x => x.EmployeeId == e.Id && x.Type == PayrollEntryType.Bonus).Sum(x => (decimal?)x.Amount) ?? 0,
                db.PayrollSettlements.Where(s => s.EmployeeId == e.Id && s.Status == VoucherStatus.Posted).Max(s => (DateOnly?)s.PeriodTo)))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PayrollCandidateDto>> CandidatesAsync(CancellationToken ct = default) =>
        await db.Users.AsNoTracking().Where(u => u.IsActive && !db.Employees.Any(e => e.UserId == u.Id)).OrderBy(u => u.FullName)
            .Select(u => new PayrollCandidateDto(u.Id, u.FullName, u.UserName!, u.PhoneNumber)).ToListAsync(ct);

    public async Task<EmployeeDto> GetAsync(int id, CancellationToken ct = default) =>
        await LoadAsync(e => e.Id == id, ct) ?? throw new NotFoundException("العامل غير موجود");

    public async Task<EmployeeDto?> MineAsync(CancellationToken ct = default)
    {
        var me = currentUser.RequiredUserId;
        var mine = await LoadAsync(e => e.UserId == me, ct);
        // The employee sees what they were paid, not which of the store's accounts paid it.
        return mine is null ? null : mine with
        {
            Entries = mine.Entries.Select(x => x with { AccountName = null, ExpenseNumber = null }).ToList(),
            Settlements = mine.Settlements.Select(x => x with { AccountName = null, ExpenseNumber = null }).ToList(),
        };
    }

    private async Task<EmployeeDto?> LoadAsync(System.Linq.Expressions.Expression<Func<Employee, bool>> which, CancellationToken ct)
    {
        var e = await db.Employees.AsNoTracking().Where(which)
            .Select(e => new { e.Id, e.UserId, e.User.FullName, UserName = e.User.UserName!, e.User.PhoneNumber, e.PayPeriod, e.Rate, e.StartDate, e.Notes, e.IsActive })
            .FirstOrDefaultAsync(ct);
        if (e is null) return null;

        var users = db.Users;
        var entries = await db.PayrollEntries.AsNoTracking().Where(x => x.EmployeeId == e.Id).OrderByDescending(x => x.Date).ThenByDescending(x => x.Id)
            .Select(x => new PayrollEntryDto(x.Id, x.Type, x.Date, x.Amount, x.Notes,
                db.PaymentVouchers.Where(v => v.Id == x.PaymentVoucherId).Select(v => v.FinancialAccount.Name).FirstOrDefault()
                    ?? db.Expenses.Where(ex => ex.Id == x.ExpenseId).Select(ex => ex.FinancialAccount.Name).FirstOrDefault(),
                db.PaymentVouchers.Where(v => v.Id == x.PaymentVoucherId).Select(v => v.Number).FirstOrDefault()
                    ?? db.Expenses.Where(ex => ex.Id == x.ExpenseId).Select(ex => ex.Number).FirstOrDefault(),
                x.CarriedFromSettlementId != null, x.SettlementId,
                db.PayrollSettlements.Where(s => s.Id == x.SettlementId).Select(s => s.Number).FirstOrDefault(),
                x.Status, x.VoidReason, users.Where(u => u.Id == x.CreatedById).Select(u => u.FullName).FirstOrDefault()))
            .ToListAsync(ct);
        var settlements = await db.PayrollSettlements.AsNoTracking().Where(s => s.EmployeeId == e.Id).OrderByDescending(s => s.PeriodTo).ThenByDescending(s => s.Id)
            .Select(s => new PayrollSettlementDto(s.Id, s.Number, s.Date, s.PeriodFrom, s.PeriodTo, s.PayPeriod, s.Units, s.Rate,
                s.Gross, s.Bonuses, s.Deductions, s.Advances, s.Net, s.CarriedAdvance,
                db.FinancialAccounts.Where(a => a.Id == s.FinancialAccountId).Select(a => a.Name).FirstOrDefault(),
                db.Expenses.Where(ex => ex.Id == s.ExpenseId).Select(ex => ex.Number).FirstOrDefault(),
                s.Notes, s.Status, s.VoidReason, users.Where(u => u.Id == s.CreatedById).Select(u => u.FullName).FirstOrDefault()))
            .ToListAsync(ct);

        var open = entries.Where(x => x.SettlementId == null && x.Status == VoucherStatus.Posted).ToList();
        return new EmployeeDto(e.Id, e.UserId, e.FullName, e.UserName, e.PhoneNumber, e.PayPeriod, e.Rate, e.StartDate, e.Notes, e.IsActive,
            open.Where(x => x.Type == PayrollEntryType.Advance).Sum(x => x.Amount),
            open.Where(x => x.Type == PayrollEntryType.Deduction).Sum(x => x.Amount),
            open.Where(x => x.Type == PayrollEntryType.Bonus).Sum(x => x.Amount),
            settlements.Where(s => s.Status == VoucherStatus.Posted).Select(s => (DateOnly?)s.PeriodTo).Max(),
            entries, settlements);
    }

    // ------------------------------------------------------------------ employees

    public async Task<EmployeeDto> CreateAsync(SaveEmployeeRequest request, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == request.UserId, ct) ?? throw new ValidationFailedException("userId", "المستخدم غير موجود");
        if (await db.Employees.AnyAsync(e => e.UserId == request.UserId, ct))
            throw new BusinessRuleException($"«{user.FullName}» مسجّل في الرواتب من قبل");
        var employee = new Employee { UserId = user.Id };
        Apply(employee, request);
        user.PhoneNumber = Customers.PhoneNumbers.Normalize(request.Phone) ?? user.PhoneNumber;
        db.Employees.Add(employee);
        await db.SaveChangesAsync(ct);
        audit.Log("Payroll.AddEmployee", "Employee", employee.Id.ToString(), null, new { user.FullName, employee.PayPeriod, employee.Rate });
        await db.SaveChangesAsync(ct);
        return await GetAsync(employee.Id, ct);
    }

    public async Task<EmployeeDto> UpdateAsync(int id, SaveEmployeeRequest request, CancellationToken ct = default)
    {
        var employee = await db.Employees.Include(e => e.User).FirstOrDefaultAsync(e => e.Id == id, ct) ?? throw new NotFoundException("العامل غير موجود");
        var before = new { employee.PayPeriod, employee.Rate, employee.IsActive };
        Apply(employee, request);
        employee.User.PhoneNumber = Customers.PhoneNumbers.Normalize(request.Phone);
        audit.Log("Payroll.UpdateEmployee", "Employee", id.ToString(), before, new { employee.PayPeriod, employee.Rate, employee.IsActive });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task RemoveAsync(int id, CancellationToken ct = default)
    {
        var employee = await db.Employees.Include(e => e.User).FirstOrDefaultAsync(e => e.Id == id, ct) ?? throw new NotFoundException("العامل غير موجود");
        if (await db.PayrollEntries.AnyAsync(e => e.EmployeeId == id, ct) || await db.PayrollSettlements.AnyAsync(s => s.EmployeeId == id, ct))
            throw new BusinessRuleException("للعامل حركات رواتب مسجّلة، فلا يُحذف؛ اجعله «متوقف» بدلًا من ذلك");
        db.Employees.Remove(employee);
        audit.Log("Payroll.RemoveEmployee", "Employee", id.ToString(), new { employee.User.FullName, employee.PayPeriod, employee.Rate }, null);
        await db.SaveChangesAsync(ct);
    }

    private static void Apply(Employee employee, SaveEmployeeRequest request)
    {
        employee.PayPeriod = request.PayPeriod;
        employee.Rate = request.Rate;
        employee.StartDate = request.StartDate;
        employee.Notes = Clean(request.Notes);
        employee.IsActive = request.IsActive;
    }

    // ------------------------------------------------------------------ advances, deductions, bonuses

    public async Task<EmployeeDto> AddEntryAsync(int employeeId, CreatePayrollEntryRequest request, CancellationToken ct = default)
    {
        const string operation = "Payroll.Entry";
        if (await idempotency.FindAsync(request.IdempotencyKey, operation, ct) is not null) return await GetAsync(employeeId, ct);
        var employee = await db.Employees.Include(e => e.User).FirstOrDefaultAsync(e => e.Id == employeeId, ct) ?? throw new NotFoundException("العامل غير موجود");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var date = await DocumentDates.ToUtcAsync(request.Date, clock, db, ct);
        var entry = new PayrollEntry { EmployeeId = employee.Id, Type = request.Type, Date = date, Amount = request.Amount, Notes = Clean(request.Notes) };

        // An advance is money handed over now, owed by the employee: it leaves the account, and becomes a salary
        // expense only when a settlement takes it off the salary.
        if (request.Type == PayrollEntryType.Advance)
        {
            var voucher = await vouchers.PostAdvanceAsync($"سلفة — {employee.User.FullName}", request.Amount, request.FinancialAccountId!.Value, date, entry.Notes, ct);
            entry.PaymentVoucherId = voucher.Id;
        }

        db.PayrollEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        audit.Log("Payroll.Entry", "PayrollEntry", entry.Id.ToString(), null, new { employee.User.FullName, entry.Type, entry.Amount, entry.Notes });
        idempotency.Record(request.IdempotencyKey, operation, "PayrollEntry", entry.Id.ToString());
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        // The employee hears about anything recorded against their salary (no account names).
        var title = request.Type switch
        {
            PayrollEntryType.Advance => $"تم صرف سلفة لك — {PushMessage.Money(entry.Amount)}",
            PayrollEntryType.Deduction => $"خصم من راتبك — {PushMessage.Money(entry.Amount)}",
            _ => $"مكافأة لك — {PushMessage.Money(entry.Amount)}",
        };
        var body = new List<string>
        {
            request.Type == PayrollEntryType.Bonus ? "تُضاف إلى مخالصتك القادمة" : "تُخصم من مخالصتك القادمة",
        };
        if (entry.Notes is not null) body.Insert(0, request.Type == PayrollEntryType.Deduction ? $"السبب: {entry.Notes}" : entry.Notes);
        push.NotifyUser(employee.UserId, new PushMessage(title, string.Join('\n', body), "/account/payroll", $"payroll-entry-{entry.Id}"));
        return await GetAsync(employee.Id, ct);
    }

    public async Task<EmployeeDto> VoidEntryAsync(int entryId, VoidDocumentRequest request, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var entry = await db.PayrollEntries.FirstOrDefaultAsync(e => e.Id == entryId, ct) ?? throw new NotFoundException("الحركة غير موجودة");
        if (entry.Status == VoucherStatus.Voided) throw new BusinessRuleException("الحركة ملغاة من قبل");
        if (entry.SettlementId is not null) throw new BusinessRuleException("دخلت هذه الحركة في مخالصة؛ ألغِ المخالصة أولًا");
        if (entry.CarriedFromSettlementId is not null) throw new BusinessRuleException("هذا باقي سلف من مخالصة؛ يُلغى بإلغاء تلك المخالصة");

        var reason = request.Reason.Trim();
        if (entry.PaymentVoucherId is { } voucherId) await vouchers.VoidPaymentCoreAsync(voucherId, reason, ct);
        if (entry.ExpenseId is { } expenseId) await vouchers.VoidExpenseCoreAsync(expenseId, reason, ct);
        entry.Status = VoucherStatus.Voided;
        entry.VoidedAt = clock.UtcNow;
        entry.VoidReason = reason;
        audit.Log("Payroll.VoidEntry", "PayrollEntry", entryId.ToString(), null, new { entry.Type, entry.Amount }, reason);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(entry.EmployeeId, ct);
    }

    // ------------------------------------------------------------------ settlements

    public async Task<EmployeeDto> SettleAsync(int employeeId, CreateSettlementRequest request, CancellationToken ct = default)
    {
        const string operation = "Payroll.Settle";
        if (await idempotency.FindAsync(request.IdempotencyKey, operation, ct) is not null) return await GetAsync(employeeId, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // One settlement at a time per employee: the open entries it takes must not be taken twice.
        await db.Database.ExecuteSqlAsync($"SELECT Id FROM Employees WITH (UPDLOCK, ROWLOCK) WHERE Id = {employeeId}", ct);
        var employee = await db.Employees.Include(e => e.User).FirstOrDefaultAsync(e => e.Id == employeeId, ct) ?? throw new NotFoundException("العامل غير موجود");

        if (await db.PayrollSettlements.AnyAsync(s => s.EmployeeId == employee.Id && s.Status == VoucherStatus.Posted
                && s.PeriodFrom <= request.PeriodTo && s.PeriodTo >= request.PeriodFrom, ct))
            throw new BusinessRuleException("يوجد مخالصة لهذا العامل تشمل جزءًا من هذه الفترة");

        // Never more days, weeks or months than the period holds (a month counts as at least 28 days).
        var days = request.PeriodTo.DayNumber - request.PeriodFrom.DayNumber + 1;
        var (most, unit) = employee.PayPeriod switch
        {
            PayPeriod.Daily => ((decimal)days, "الأيام"),
            PayPeriod.Weekly => (Math.Ceiling(days / 7m * 100) / 100, "الأسابيع"),
            _ => (Math.Ceiling(days / 28m * 100) / 100, "الأشهر"),
        };
        if (request.Units > most + 0.001m)
            throw new ValidationFailedException("units", $"عدد {unit} ({request.Units:0.##}) أكبر مما في الفترة ({most:0.##})");

        var open = await db.PayrollEntries.Where(e => e.EmployeeId == employee.Id && e.SettlementId == null && e.Status == VoucherStatus.Posted).ToListAsync(ct);
        var gross = CostMath.RoundMoney(request.Units * employee.Rate);
        var bonuses = open.Where(e => e.Type == PayrollEntryType.Bonus).Sum(e => e.Amount);
        var deductions = open.Where(e => e.Type == PayrollEntryType.Deduction).Sum(e => e.Amount);
        var advances = open.Where(e => e.Type == PayrollEntryType.Advance).Sum(e => e.Amount);
        var due = gross + bonuses - deductions - advances;
        var net = Math.Max(0, due);
        var carried = due < 0 ? -due : 0;
        // Advances were paid earlier and owed by the employee; the part this salary covers becomes a salary expense now.
        // (Advances recorded before this rule were already counted as an expense when paid.)
        var advanceExpense = Math.Max(0, open.Where(e => e.Type == PayrollEntryType.Advance && e.ExpenseId == null).Sum(e => e.Amount) - carried);
        if (net > 0 && request.FinancialAccountId is null)
            throw new ValidationFailedException("financialAccountId", "اختر الحساب الذي يُصرف منه الراتب");

        var now = clock.UtcNow;
        var settlement = new PayrollSettlement
        {
            Number = await numbers.NextAsync(DocumentType.PayrollSettlement, ct),
            EmployeeId = employee.Id,
            Date = now,
            PeriodFrom = request.PeriodFrom,
            PeriodTo = request.PeriodTo,
            PayPeriod = employee.PayPeriod,
            Units = request.Units,
            Rate = employee.Rate,
            Gross = gross,
            Bonuses = bonuses,
            Deductions = deductions,
            Advances = advances,
            Net = net,
            CarriedAdvance = carried,
            AdvanceExpense = advanceExpense,
            FinancialAccountId = net > 0 ? request.FinancialAccountId : null,
            Notes = Clean(request.Notes),
        };
        db.PayrollSettlements.Add(settlement);
        await db.SaveChangesAsync(ct);

        if (net > 0)
        {
            var expense = await vouchers.PostExpenseAsync(await SalaryCategoryAsync(ct),
                $"راتب {employee.User.FullName} من {request.PeriodFrom:yyyy/MM/dd} إلى {request.PeriodTo:yyyy/MM/dd} ({settlement.Number})",
                net, request.FinancialAccountId!.Value, now, ct);
            settlement.ExpenseId = expense.Id;
        }
        foreach (var e in open) e.SettlementId = settlement.Id;
        // Advances bigger than the salary are not lost: what is left waits for the next settlement.
        if (carried > 0)
            db.PayrollEntries.Add(new PayrollEntry
            {
                EmployeeId = employee.Id, Type = PayrollEntryType.Advance, Date = now, Amount = carried,
                Notes = $"باقي سلف من المخالصة {settlement.Number}", CarriedFromSettlementId = settlement.Id,
            });

        audit.Log("Payroll.Settle", "PayrollSettlement", settlement.Id.ToString(), null,
            new { settlement.Number, employee.User.FullName, gross, bonuses, deductions, advances, net, carried });
        idempotency.Record(request.IdempotencyKey, operation, "PayrollSettlement", settlement.Id.ToString());
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        // The employee's own phone: what was paid and how it was worked out (no account names).
        var lines = new List<string> { $"الفترة: {request.PeriodFrom:yyyy/MM/dd} — {request.PeriodTo:yyyy/MM/dd}", $"الراتب: {PushMessage.Money(gross)}" };
        if (bonuses > 0) lines.Add($"مكافآت: + {PushMessage.Money(bonuses)}");
        if (deductions > 0) lines.Add($"خصومات: − {PushMessage.Money(deductions)}");
        if (advances > 0) lines.Add($"سلف: − {PushMessage.Money(advances)}");
        if (carried > 0) lines.Add($"باقي سلف تُخصم من المخالصة القادمة: {PushMessage.Money(carried)}");
        push.NotifyUser(employee.UserId, new PushMessage(
            net > 0 ? $"تم صرف راتبك — {PushMessage.Money(net)}" : "تمت مخالصة راتبك",
            string.Join('\n', lines), "/account/payroll", $"salary-{settlement.Id}"));
        return await GetAsync(employee.Id, ct);
    }

    public async Task<EmployeeDto> VoidSettlementAsync(int settlementId, VoidDocumentRequest request, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var settlement = await db.PayrollSettlements.FirstOrDefaultAsync(s => s.Id == settlementId, ct) ?? throw new NotFoundException("المخالصة غير موجودة");
        if (settlement.Status == VoucherStatus.Voided) throw new BusinessRuleException("المخالصة ملغاة من قبل");

        var carried = await db.PayrollEntries.Where(e => e.CarriedFromSettlementId == settlement.Id && e.Status == VoucherStatus.Posted).ToListAsync(ct);
        if (carried.Any(e => e.SettlementId is not null))
            throw new BusinessRuleException("باقي السلف من هذه المخالصة دخل في مخالصة لاحقة؛ ألغِ المخالصة اللاحقة أولًا");

        var reason = request.Reason.Trim();
        var now = clock.UtcNow;
        if (settlement.ExpenseId is { } expenseId) await vouchers.VoidExpenseCoreAsync(expenseId, reason, ct);
        foreach (var e in carried)
        {
            e.Status = VoucherStatus.Voided;
            e.VoidedAt = now;
            e.VoidReason = reason;
        }
        // What it took (advances, deductions, bonuses) is open again for the next settlement.
        await db.PayrollEntries.Where(e => e.SettlementId == settlement.Id).ExecuteUpdateAsync(s => s.SetProperty(e => e.SettlementId, (int?)null), ct);
        settlement.Status = VoucherStatus.Voided;
        settlement.VoidedAt = now;
        settlement.VoidReason = reason;
        audit.Log("Payroll.VoidSettlement", "PayrollSettlement", settlementId.ToString(), null, new { settlement.Number, settlement.Net }, reason);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(settlement.EmployeeId, ct);
    }

    /// <summary>The «الرواتب» expense type, so salaries and advances show under it in the expenses and reports.</summary>
    private async Task<int> SalaryCategoryAsync(CancellationToken ct)
    {
        var id = await db.ExpenseCategories.Where(c => c.Name == SalaryCategory).Select(c => (int?)c.Id).FirstOrDefaultAsync(ct);
        if (id is { } found) return found;
        var category = new ExpenseCategory { Name = SalaryCategory };
        db.ExpenseCategories.Add(category);
        await db.SaveChangesAsync(ct);
        return category.Id;
    }

    private static string Suffix(string? notes) => notes is null ? "" : $" ({notes})";
    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
