using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Qwader.Application.Abstractions;
using Qwader.Domain.Administration;
using Qwader.Infrastructure.Persistence;

namespace Qwader.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class DocumentNumberTests(ApiFactory factory)
{
    private async Task<string> AllocateAsync(DocumentType type, bool commit = true)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var numbers = scope.ServiceProvider.GetRequiredService<IDocumentNumberGenerator>();

        await using var tx = await db.Database.BeginTransactionAsync();
        var number = await numbers.NextAsync(type);
        if (commit) await tx.CommitAsync();
        else await tx.RollbackAsync();
        return number;
    }

    [Fact]
    public async Task Concurrent_allocations_never_produce_duplicates_or_gaps()
    {
        const int count = 25;

        var numbers = await Task.WhenAll(Enumerable.Range(0, count).Select(_ => Task.Run(() => AllocateAsync(DocumentType.StockCount))));

        numbers.Should().OnlyHaveUniqueItems();
        var sequence = numbers.Select(n => int.Parse(n[^6..])).Order().ToList();
        sequence.Should().Equal(Enumerable.Range(sequence[0], count));
        numbers.Should().AllSatisfy(n => n.Should().MatchRegex(@"^SC-\d{4}-\d{6}$"));
    }

    [Fact]
    public async Task Rolled_back_transaction_releases_its_number()
    {
        var first = await AllocateAsync(DocumentType.StockAdjustment);
        await AllocateAsync(DocumentType.StockAdjustment, commit: false);
        var next = await AllocateAsync(DocumentType.StockAdjustment);

        int.Parse(next[^6..]).Should().Be(int.Parse(first[^6..]) + 1);
    }

    [Fact]
    public async Task Allocation_outside_a_transaction_is_refused()
    {
        using var scope = factory.Services.CreateScope();
        var numbers = scope.ServiceProvider.GetRequiredService<IDocumentNumberGenerator>();

        var act = () => numbers.NextAsync(DocumentType.Expense);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
