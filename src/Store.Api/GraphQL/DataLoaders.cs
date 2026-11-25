using Microsoft.EntityFrameworkCore;
using Store.Api.Data;

namespace Store.Api.GraphQL;

// Source-generated DataLoaders. Each method receives every key requested while the
// resolvers of one level run, deduplicated, and sends one SQL command for all of them.
internal static class DataLoaders
{
    [DataLoader]
    public static async Task<Dictionary<int, Brand>> GetBrandByIdAsync(
        IReadOnlyList<int> ids, IDbContextFactory<StoreDbContext> dbFactory, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Brands.AsNoTracking()
            .Where(b => ids.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, ct);
    }

    [DataLoader]
    public static async Task<Dictionary<int, Product>> GetProductByIdAsync(
        IReadOnlyList<int> ids, IDbContextFactory<StoreDbContext> dbFactory, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Products.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);
    }

    [DataLoader]
    public static async Task<Dictionary<int, Review[]>> GetReviewsByProductIdAsync(
        IReadOnlyList<int> productIds, IDbContextFactory<StoreDbContext> dbFactory, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var reviews = await db.Reviews.AsNoTracking()
            .Where(r => productIds.Contains(r.ProductId))
            .OrderBy(r => r.Id)
            .ToListAsync(ct);
        return reviews.GroupBy(r => r.ProductId).ToDictionary(g => g.Key, g => g.ToArray());
    }

    [DataLoader]
    public static async Task<Dictionary<int, Product[]>> GetProductsByBrandIdAsync(
        IReadOnlyList<int> brandIds, IDbContextFactory<StoreDbContext> dbFactory, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var products = await db.Products.AsNoTracking()
            .Where(p => brandIds.Contains(p.BrandId))
            .OrderBy(p => p.Id)
            .ToListAsync(ct);
        return products.GroupBy(p => p.BrandId).ToDictionary(g => g.Key, g => g.ToArray());
    }
}
