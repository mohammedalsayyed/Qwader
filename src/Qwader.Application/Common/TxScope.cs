using Microsoft.EntityFrameworkCore.Storage;
using Qwader.Application.Abstractions;

namespace Qwader.Application.Common;

/// <summary>
/// A transaction that joins one already open: editing an invoice voids the old one and makes the new one inside a single
/// transaction, through the same void and create code, so either both happen or neither does. On its own it is an ordinary
/// transaction; inside another, committing and disposing are left to the outer one.
/// </summary>
public sealed class TxScope : IAsyncDisposable
{
    private readonly IDbContextTransaction? owned;

    private TxScope(IDbContextTransaction? owned) => this.owned = owned;

    public static async Task<TxScope> BeginAsync(IAppDbContext db, CancellationToken ct) =>
        new(db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null);

    public Task CommitAsync(CancellationToken ct) => owned?.CommitAsync(ct) ?? Task.CompletedTask;

    public ValueTask DisposeAsync() => owned?.DisposeAsync() ?? ValueTask.CompletedTask;
}
