using Qwader.Domain.Common;
using Qwader.Domain.Finance;
using Qwader.Domain.Identity;

namespace Qwader.Domain.Payroll;

public enum PayPeriod
{
    Daily = 1,
    Weekly = 2,
    Monthly = 3,
}

public enum PayrollEntryType
{
    /// <summary>Money handed to the employee ahead of the salary; paid from an account at once, taken off the next settlement.</summary>
    Advance = 1,
    /// <summary>Taken off the next settlement (absence, damage…); no money moves.</summary>
    Deduction = 2,
    /// <summary>Added to the next settlement; no money moves until then.</summary>
    Bonus = 3,
}

/// <summary>A system user who is paid a salary: how often and how much.</summary>
public class Employee : AuditableEntity
{
    public int UserId { get; set; }
    public PayPeriod PayPeriod { get; set; } = PayPeriod.Monthly;
    /// <summary>Salary for one period (a day, a week or a month).</summary>
    public decimal Rate { get; set; }
    public DateOnly? StartDate { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;

    public AppUser User { get; set; } = null!;
}

/// <summary>An advance, deduction or bonus waiting for the employee's next settlement (or already settled by one).</summary>
public class PayrollEntry : AuditableEntity
{
    public int EmployeeId { get; set; }
    public PayrollEntryType Type { get; set; }
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
    /// <summary>An advance paid out: the salary expense (and payment voucher) that took the money from the account.</summary>
    public int? ExpenseId { get; set; }
    /// <summary>Left over from a settlement whose advances were larger than the salary; no money moved for it.</summary>
    public int? CarriedFromSettlementId { get; set; }
    /// <summary>The settlement that took it into account; null while it is still open.</summary>
    public int? SettlementId { get; set; }
    public VoucherStatus Status { get; set; } = VoucherStatus.Posted;
    public DateTime? VoidedAt { get; set; }
    public string? VoidReason { get; set; }

    public Employee Employee { get; set; } = null!;
}

/// <summary>
/// «مخالصة»: one pay period closed. Gross = units × rate; net = gross + bonuses − deductions − advances, paid from an account.
/// Advances larger than the salary leave the rest as a new open advance.
/// </summary>
public class PayrollSettlement : AuditableEntity
{
    public string Number { get; set; } = string.Empty;
    public int EmployeeId { get; set; }
    public DateTime Date { get; set; }
    public DateOnly PeriodFrom { get; set; }
    public DateOnly PeriodTo { get; set; }
    public PayPeriod PayPeriod { get; set; }
    /// <summary>Days, weeks or months paid.</summary>
    public decimal Units { get; set; }
    public decimal Rate { get; set; }
    public decimal Gross { get; set; }
    public decimal Bonuses { get; set; }
    public decimal Deductions { get; set; }
    public decimal Advances { get; set; }
    /// <summary>Paid now.</summary>
    public decimal Net { get; set; }
    /// <summary>Advances the salary did not cover: carried to the next settlement.</summary>
    public decimal CarriedAdvance { get; set; }
    public int? FinancialAccountId { get; set; }
    public int? ExpenseId { get; set; }
    public string? Notes { get; set; }
    public VoucherStatus Status { get; set; } = VoucherStatus.Posted;
    public DateTime? VoidedAt { get; set; }
    public string? VoidReason { get; set; }

    public Employee Employee { get; set; } = null!;
}
