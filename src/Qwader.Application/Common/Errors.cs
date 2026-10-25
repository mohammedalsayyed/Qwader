namespace Qwader.Application.Common;

/// <summary>Base for expected failures. Messages are Arabic and shown to the user as-is.</summary>
public abstract class AppException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed class NotFoundException(string message = "العنصر المطلوب غير موجود") : AppException(message, 404);

public sealed class UnauthorizedException(string message = "يرجى تسجيل الدخول") : AppException(message, 401);

public sealed class ForbiddenException(string message = "ليست لديك صلاحية لتنفيذ هذه العملية") : AppException(message, 403);

/// <summary>Request conflicts with current state (duplicate code, stale version, …).</summary>
public sealed class ConflictException(string message) : AppException(message, 409);

/// <summary>A business rule rejected the operation (insufficient stock, price below minimum, …).</summary>
public sealed class BusinessRuleException(string message) : AppException(message, 422);

public sealed class ValidationFailedException : AppException
{
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public ValidationFailedException(IReadOnlyDictionary<string, string[]> errors)
        : base("يرجى تصحيح الحقول المشار إليها", 400) => Errors = errors;

    public ValidationFailedException(string field, string message)
        : this(new Dictionary<string, string[]> { [field] = [message] }) { }
}
