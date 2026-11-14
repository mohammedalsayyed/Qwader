using Microsoft.EntityFrameworkCore;
using Qwader.Application.Abstractions;

namespace Qwader.Application.Common;

/// <summary>
/// Serializes operations on one document. Every operation that changes a document's state (void, post, return, restore…)
/// locks its row first, inside its transaction: a second request for the same document — a double tap, two tabs, a retry —
/// waits for the first to commit, then reads the new state and is refused («ملغاة من قبل») instead of reversing it twice.
/// </summary>
public static class RowLocks
{
    public static Task LockRowAsync(this IAppDbContext db, string table, int id, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync($"SELECT Id FROM [{table}] WITH (UPDLOCK, ROWLOCK) WHERE Id = {{0}}", [id], ct);
}
