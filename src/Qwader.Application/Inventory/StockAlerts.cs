using Microsoft.EntityFrameworkCore;
using Qwader.Application.Abstractions;
using Qwader.Application.Notifications;
using Qwader.Domain.Identity;
using Qwader.Domain.Notifications;

namespace Qwader.Application.Inventory;

/// <summary>Stock alerts: only the movement that crosses the line raises one, not every later one on a product already low.</summary>
public static class StockAlerts
{
    /// <param name="removed">Base units that just left, per product (a sale, damaged, lost, a count short…).</param>
    public static async Task NotifyCrossedAsync(IAppDbContext db, IPushNotifier push, IReadOnlyDictionary<int, decimal> removed, CancellationToken ct)
    {
        var ids = removed.Where(r => r.Value > 0).Select(r => r.Key).ToList();
        if (ids.Count == 0) return;
        var stock = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, p.ReorderLevel, Quantity = p.StockBalances.Sum(b => b.Quantity) }).ToListAsync(ct);
        foreach (var p in stock)
        {
            var before = p.Quantity + removed[p.Id];
            if (p.Quantity <= 0 && before > 0)
                push.Notify(PushTopics.Stock, new PushMessage("نفد من المخزون", p.Name, $"/products/{p.Id}", $"stock-{p.Id}"), Permissions.InventoryView);
            else if (p.Quantity <= p.ReorderLevel && before > p.ReorderLevel)
                push.Notify(PushTopics.Stock, new PushMessage("قارب على النفاد", $"{p.Name} — باقي {p.Quantity:0.##}", $"/products/{p.Id}", $"stock-{p.Id}"),
                    Permissions.InventoryView);
        }
    }
}
