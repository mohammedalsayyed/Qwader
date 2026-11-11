using FluentValidation;

namespace Qwader.Application.Settings;

public enum SystemResetMode
{
    /// <summary>Sales, purchases, transfers, vouchers, expenses and stock movements go; products, customers, suppliers and accounts stay with zero quantities and balances.</summary>
    TransactionsOnly = 1,

    /// <summary>All business data goes; only users, roles, settings and reference lists (units, warehouses, expense categories) stay.</summary>
    Everything = 2,
}

public sealed record SystemResetRequest(SystemResetMode Mode, string Password);

public sealed record SystemResetResult(SystemResetMode Mode, int DeletedRows);

public interface ISystemResetService
{
    /// <summary>Wipes data for a fresh start. Admin only, re-confirmed with the caller's own password. Cannot be undone.</summary>
    Task<SystemResetResult> ResetAsync(SystemResetRequest request, CancellationToken ct = default);

    /// <summary>
    /// Deletes the audit log entries of a period (both days included). Admin only, re-confirmed with the caller's password;
    /// the deletion itself is then logged (who, which period, how many).
    /// </summary>
    Task<int> DeleteAuditLogAsync(DeleteAuditLogRequest request, CancellationToken ct = default);
}

public sealed record DeleteAuditLogRequest(DateOnly From, DateOnly To, string Password);

public sealed class DeleteAuditLogRequestValidator : AbstractValidator<DeleteAuditLogRequest>
{
    public DeleteAuditLogRequestValidator()
    {
        RuleFor(x => x.Password).NotEmpty().WithMessage("أدخل كلمة المرور للتأكيد");
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).WithMessage("تاريخ النهاية قبل تاريخ البداية");
    }
}

public sealed class SystemResetRequestValidator : AbstractValidator<SystemResetRequest>
{
    public SystemResetRequestValidator()
    {
        RuleFor(x => x.Mode).IsInEnum().WithMessage("اختر نوع التصفير");
        RuleFor(x => x.Password).NotEmpty().WithMessage("اكتب كلمة المرور للتأكيد");
    }
}
