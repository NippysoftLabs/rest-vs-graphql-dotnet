using HotChocolate;
using HotChocolate.CostAnalysis.Types;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using Store.Api.Data;

namespace Store.Api.GraphQL;

// GraphQL: one endpoint, and the client decides which fields come back.
[QueryType]
public static partial class Query
{
    // ListSize tells cost analysis that this list holds up to "first" items (at most 50).
    [ListSize(
        AssumedSize = Limits.MaxPageSize,
        SlicingArguments = ["first"],
        RequireOneSlicingArgument = false,
        SlicingArgumentDefaultValue = Limits.DefaultPageSize)]
    public static async Task<IReadOnlyList<Product>> GetProductsAsync(
        IDbContextFactory<StoreDbContext> dbFactory, CancellationToken ct, int first = Limits.DefaultPageSize)
    {
        if (first is < 1 or > Limits.MaxPageSize)
        {
            throw new GraphQLException(ErrorBuilder.New()
                .SetMessage($"first must be between 1 and {Limits.MaxPageSize}.")
                .SetCode("STORE_INVALID_FIRST")
                .Build());
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Products.AsNoTracking()
            .OrderBy(p => p.Id)
            .Take(first)
            .ToListAsync(ct);
    }

    public static async Task<Product?> GetProductByIdAsync(
        int id, IProductByIdDataLoader productById, CancellationToken ct)
        => await productById.LoadAsync(id, ct);
}
