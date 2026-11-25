using Microsoft.EntityFrameworkCore;
using Store.Api.Data;

namespace Store.Api.Rest;

// REST: one resource per URL, and the server decides the shape of each response.
public static class RestEndpoints
{
    public sealed record ProductResource(int Id, string Name, string Description, decimal Price, int BrandId);

    public sealed record BrandResource(int Id, string Name, string Country);

    public sealed record ReviewResource(int Id, int Rating, string Comment);

    public sealed record ProductDetailResource(
        int Id, string Name, string Description, decimal Price, BrandResource Brand, IReadOnlyList<ReviewResource> Reviews);

    public sealed record NewReview(int Rating, string? Comment);

    public static IEndpointRouteBuilder MapRestApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/products", GetProductsAsync);
        api.MapGet("/products/{id:int}", GetProductAsync);
        api.MapGet("/brands/{id:int}", GetBrandAsync);
        api.MapPost("/products/{id:int}/reviews", AddReviewAsync);

        return app;
    }

    private static async Task<IResult> GetProductsAsync(
        IDbContextFactory<StoreDbContext> dbFactory, HttpContext http, CancellationToken ct,
        int page = 1, int pageSize = Limits.DefaultPageSize)
    {
        // Report only the parameters that are out of range.
        var errors = new Dictionary<string, string[]>();
        if (page is < 1 or > Limits.MaxPage)
        {
            errors["page"] = [$"page must be between 1 and {Limits.MaxPage}."];
        }

        if (pageSize is < 1 or > Limits.MaxPageSize)
        {
            errors["pageSize"] = [$"pageSize must be between 1 and {Limits.MaxPageSize}."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var items = await db.Products.AsNoTracking()
            .OrderBy(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new ProductResource(p.Id, p.Name, p.Description, p.Price, p.BrandId))
            .ToListAsync(ct);

        // Public catalog data: any HTTP cache (browser, proxy, CDN) may reuse it for 60 s.
        http.Response.Headers.CacheControl = "public, max-age=60";
        return Results.Ok(items);
    }

    private static async Task<IResult> GetProductAsync(
        int id, IDbContextFactory<StoreDbContext> dbFactory, HttpContext http, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // One SQL command: the product joined with its brand.
        var row = await db.Products.AsNoTracking()
            .Where(p => p.Id == id)
            .Join(db.Brands, p => p.BrandId, b => b.Id, (p, b) => new { Product = p, Brand = b })
            .SingleOrDefaultAsync(ct);
        if (row is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Product not found.");
        }

        // A second SQL command: the reviews of that product.
        var reviews = await db.Reviews.AsNoTracking()
            .Where(r => r.ProductId == id)
            .OrderBy(r => r.Id)
            .Select(r => new ReviewResource(r.Id, r.Rating, r.Comment))
            .ToListAsync(ct);

        http.Response.Headers.CacheControl = "public, max-age=60";
        return Results.Ok(new ProductDetailResource(
            row.Product.Id, row.Product.Name, row.Product.Description, row.Product.Price,
            new BrandResource(row.Brand.Id, row.Brand.Name, row.Brand.Country), reviews));
    }

    private static async Task<IResult> GetBrandAsync(
        int id, IDbContextFactory<StoreDbContext> dbFactory, HttpContext http, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var brand = await db.Brands.AsNoTracking()
            .Where(b => b.Id == id)
            .Select(b => new BrandResource(b.Id, b.Name, b.Country))
            .SingleOrDefaultAsync(ct);
        if (brand is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Brand not found.");
        }

        http.Response.Headers.CacheControl = "public, max-age=300";
        return Results.Ok(brand);
    }

    private static async Task<IResult> AddReviewAsync(
        int id, NewReview body, IDbContextFactory<StoreDbContext> dbFactory, CancellationToken ct)
    {
        var error = Limits.ValidateReview(body.Rating, body.Comment);
        if (error is not null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["review"] = [error] });
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.Products.AnyAsync(p => p.Id == id, ct))
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Product not found.");
        }

        var review = new Review { ProductId = id, Rating = body.Rating, Comment = body.Comment! };
        db.Reviews.Add(review);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/products/{id}", new ReviewResource(review.Id, review.Rating, review.Comment));
    }
}
