using HotChocolate;
using HotChocolate.CostAnalysis.Types;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using Store.Api.Data;

namespace Store.Api.GraphQL;

[ObjectType<Product>]
public static partial class ProductNode
{
    // Batched: the brands of every product in the response arrive in one SQL command.
    public static async Task<Brand> GetBrandAsync(
        [Parent] Product product, IBrandByIdDataLoader brandById, CancellationToken ct)
        => await brandById.LoadAsync(product.BrandId, ct)
           ?? throw new GraphQLException($"Brand {product.BrandId} not found.");

    // Naive on purpose, to show the N+1 problem: one SQL command per product in the list.
    public static async Task<Brand> GetBrandNaiveAsync(
        [Parent] Product product, IDbContextFactory<StoreDbContext> dbFactory, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Brands.AsNoTracking().SingleAsync(b => b.Id == product.BrandId, ct);
    }

    // Reviews are not paged: cost analysis assumes up to 20 per product.
    [ListSize(AssumedSize = 20)]
    public static async Task<Review[]> GetReviewsAsync(
        [Parent] Product product, IReviewsByProductIdDataLoader reviewsByProductId, CancellationToken ct)
        => await reviewsByProductId.LoadAsync(product.Id, ct) ?? [];
}
