using System.Data;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Qwader.Application.Abstractions;
using Qwader.Application.Common;
using Qwader.Domain.Administration;
using Qwader.Infrastructure.Persistence;

namespace Qwader.Infrastructure.Services;

public sealed class SystemClock : IClock
{
    public static readonly TimeZoneInfo Palestine = ResolvePalestine();

    public DateTime UtcNow => DateTime.UtcNow;
    public TimeZoneInfo LocalZone => Palestine;

    private static TimeZoneInfo ResolvePalestine()
    {
        // IANA id works on Linux and on Windows with ICU; the Windows id is the fallback.
        foreach (var id in new[] { "Asia/Hebron", "West Bank Standard Time", "Asia/Gaza" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
        }
        throw new TimeZoneNotFoundException("Palestine time zone is not available on this machine.");
    }
}

public sealed class DocumentNumberGenerator(AppDbContext db, IClock clock) : IDocumentNumberGenerator
{
    // MERGE … HOLDLOCK serializes concurrent callers on the (type, year) row, including the very first insert.
    private const string Sql = """
        MERGE DocumentSequences WITH (HOLDLOCK) AS t
        USING (SELECT @type AS DocumentType, @year AS [Year]) AS s
            ON t.DocumentType = s.DocumentType AND t.[Year] = s.[Year]
        WHEN MATCHED THEN UPDATE SET LastNumber = t.LastNumber + 1
        WHEN NOT MATCHED THEN INSERT (DocumentType, [Year], LastNumber) VALUES (s.DocumentType, s.[Year], 1)
        OUTPUT inserted.LastNumber;
        """;

    public async Task<string> NextAsync(DocumentType type, CancellationToken ct = default)
    {
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Document numbers must be allocated inside a database transaction.");

        var localYear = clock.ToLocal(clock.UtcNow).Year;
        var year = DocumentNumbers.IsYearly(type) ? localYear : 0;

        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = Sql;
        AddParameter(command, "@type", (int)type);
        AddParameter(command, "@year", year);

        var number = (long)(await command.ExecuteScalarAsync(ct))!;
        return DocumentNumbers.Format(type, localYear, number);
    }

    private static void AddParameter(System.Data.Common.DbCommand command, string name, int value)
    {
        var p = command.CreateParameter();
        p.ParameterName = name;
        p.DbType = DbType.Int32;
        p.Value = value;
        command.Parameters.Add(p);
    }
}

public sealed class AuditService(AppDbContext db, ICurrentUser currentUser, IClock clock) : IAuditService
{
    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep Arabic readable in the log
        WriteIndented = false,
    };

    public void Log(string action, string entityType, string? entityId, object? oldValues = null, object? newValues = null, string? reason = null)
    {
        db.AuditLogs.Add(new AuditLog
        {
            UserId = currentUser.UserId,
            UserName = currentUser.UserName,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            OldValues = oldValues is null ? null : JsonSerializer.Serialize(oldValues, Json),
            NewValues = newValues is null ? null : JsonSerializer.Serialize(newValues, Json),
            Reason = reason,
            IpAddress = currentUser.IpAddress,
            OccurredAt = clock.UtcNow,
        });
    }
}

public sealed class IdempotencyService(AppDbContext db, ICurrentUser currentUser, IClock clock) : IIdempotencyService
{
    public async Task<IdempotencyRecord?> FindAsync(Guid key, string operation, CancellationToken ct = default)
    {
        var record = await db.IdempotencyRecords.AsNoTracking().FirstOrDefaultAsync(r => r.Key == key, ct);
        if (record is not null && (record.Operation != operation || record.UserId != currentUser.UserId))
            throw new ConflictException("مفتاح العملية مستخدم لعملية أخرى، أعد تحميل الصفحة وحاول مجددًا");
        return record;
    }

    public void Record(Guid key, string operation, string resultType, string resultId)
    {
        db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Key = key,
            UserId = currentUser.RequiredUserId,
            Operation = operation,
            ResultType = resultType,
            ResultId = resultId,
            CreatedAt = clock.UtcNow,
        });
    }
}
