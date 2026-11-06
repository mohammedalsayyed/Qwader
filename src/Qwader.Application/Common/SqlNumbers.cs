using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Qwader.Application.Common;

/// <summary>
/// A decimal written in raw SQL takes the model's default decimal type (money: 2 places), so 20.7143 reached the
/// database as 20.71 and 0.125 kg as 0.13. Costs and quantities sent through raw SQL go through <see cref="Exact"/>,
/// which keeps 6 places; the column then stores them at its own precision (cost 4, quantity 3).
/// </summary>
public static class SqlNumbers
{
    private static long counter;

    /// <summary>A parameter for one place in one command (use a new one for each place, even for the same value).</summary>
    public static DbParameter Exact(this DatabaseFacade database, decimal value)
    {
        var parameter = database.GetDbConnection().CreateCommand().CreateParameter();
        parameter.ParameterName = $"@exact{Interlocked.Increment(ref counter)}";
        parameter.DbType = DbType.Decimal;
        parameter.Precision = 28;
        parameter.Scale = 6;
        parameter.Value = value;
        return parameter;
    }
}
