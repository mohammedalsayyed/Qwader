using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Qwader.Application.Common;

namespace Qwader.Api.Infrastructure;

/// <summary>Turns every failure into an Arabic ProblemDetails body: { title, status, errors?, traceId }.</summary>
public sealed class AppExceptionHandler(ILogger<AppExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, title, errors) = exception switch
        {
            ValidationFailedException v => (v.StatusCode, v.Message, CamelCaseKeys(v.Errors)),
            AppException app => (app.StatusCode, app.Message, null),
            DbUpdateConcurrencyException => (409, "تم تعديل هذه البيانات من مستخدم آخر، أعد تحميل الصفحة وحاول مجددًا", null),
            DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } } =>
                (409, "توجد قيمة مكررة لبيانات يجب أن تكون فريدة", null),
            DbUpdateException { InnerException: SqlException { Number: 547 } } =>
                (422, "لا يمكن تنفيذ العملية لأنها تخالف قيود البيانات المرتبطة", null),
            OperationCanceledException when context.RequestAborted.IsCancellationRequested => (499, "تم إلغاء الطلب", null),
            _ => (500, "حدث خطأ غير متوقع في الخادم، حاول مرة أخرى", (IReadOnlyDictionary<string, string[]>?)null),
        };

        if (status >= 500) logger.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
        else logger.LogInformation("Request rejected ({Status}): {Message}", status, exception.Message);

        if (status == 499) return true;
        await ProblemWriter.WriteAsync(context, status, title, errors);
        return true;
    }

    /// <summary>Services name fields with nameof(...) (PascalCase); the client's form fields are camelCase.</summary>
    internal static IReadOnlyDictionary<string, string[]> CamelCaseKeys(IReadOnlyDictionary<string, string[]> errors) =>
        errors.GroupBy(e => string.Join('.', e.Key.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName)))
            .ToDictionary(g => g.Key, g => g.SelectMany(e => e.Value).Distinct().ToArray());
}

public static class ProblemWriter
{
    public static Task WriteAsync(HttpContext context, int status, string title, IReadOnlyDictionary<string, string[]>? errors = null)
    {
        var problem = new ProblemDetails { Status = status, Title = title };
        if (errors is not null) problem.Extensions["errors"] = errors;
        problem.Extensions["traceId"] = context.TraceIdentifier;

        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(problem, (JsonSerializerOptions?)null, "application/problem+json");
    }
}

/// <summary>
/// Runs the FluentValidation validator for every action argument that has one, and refuses a date range whose end is
/// before its start (a «from»/«to» pair, as query values or on a filter object) instead of answering with an empty list.
/// </summary>
public sealed class ValidationFilter(IServiceProvider services) : IAsyncActionFilter
{
    private static void CheckRange(object? from, object? to)
    {
        if (from is DateOnly f && to is DateOnly t && t < f)
            throw new ValidationFailedException("to", "تاريخ النهاية قبل تاريخ البداية");
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var args = context.ActionArguments;
        CheckRange(args.TryGetValue("from", out var from) ? from : null, args.TryGetValue("to", out var to) ? to : null);
        foreach (var argument in args.Values)
        {
            if (argument is null) continue;
            var type = argument.GetType();
            if (type.GetProperty("From")?.PropertyType == typeof(DateOnly?) && type.GetProperty("To")?.PropertyType == typeof(DateOnly?))
                CheckRange(type.GetProperty("From")!.GetValue(argument), type.GetProperty("To")!.GetValue(argument));
            var validatorType = typeof(IValidator<>).MakeGenericType(type);
            if (services.GetService(validatorType) is not IValidator validator) continue;

            var result = await validator.ValidateAsync(new ValidationContext<object>(argument), context.HttpContext.RequestAborted);
            if (!result.IsValid)
            {
                var errors = result.Errors
                    .GroupBy(e => JsonNamingPolicy.CamelCase.ConvertName(e.PropertyName))
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());
                throw new ValidationFailedException(errors);
            }
        }

        await next();
    }
}
