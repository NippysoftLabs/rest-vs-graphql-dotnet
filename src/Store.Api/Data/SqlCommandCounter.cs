using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Store.Api.Data;

// Demo instrumentation: counts the SQL commands EF Core sends while one HTTP request runs.
// The count lives in an AsyncLocal, so it follows the request into resolvers and DataLoader batches.
// Commands that run outside a request scope (startup, seeding) go to Unattributed.
public sealed class SqlCommandCounter : DbCommandInterceptor
{
    private static readonly AsyncLocal<StrongBox<int>?> Current = new();
    private int _unattributed;

    public int Unattributed => Volatile.Read(ref _unattributed);

    public static StrongBox<int> BeginScope()
    {
        var box = new StrongBox<int>(0);
        Current.Value = box;
        return box;
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Count();
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Count();
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Count();
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        Count();
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Count();
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Count();
        return ValueTask.FromResult(result);
    }

    private void Count()
    {
        var box = Current.Value;
        if (box is null)
        {
            Interlocked.Increment(ref _unattributed);
        }
        else
        {
            Interlocked.Increment(ref box.Value);
        }
    }
}
