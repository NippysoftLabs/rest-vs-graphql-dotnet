namespace Store.Api.Data;

// Deterministic sample catalog: 10 brands, 50 products (5 per brand), 2 reviews for each of the first 20 products.
public static class SeedData
{
    public const int BrandCount = 10;
    public const int ProductCount = 50;

    private static readonly string[] Countries = ["Colombia", "Spain", "Mexico", "Chile", "Peru"];

    public static void Fill(StoreDbContext db)
    {
        if (db.Brands.Any())
        {
            return;
        }

        for (var b = 1; b <= BrandCount; b++)
        {
            db.Brands.Add(new Brand { Id = b, Name = $"Brand {b:00}", Country = Countries[(b - 1) % Countries.Length] });
        }

        for (var p = 1; p <= ProductCount; p++)
        {
            db.Products.Add(new Product
            {
                Id = p,
                Name = $"Product {p:000}",
                Description = $"Long description of product {p:000}. A list screen does not show this text.",
                Price = 10m + p,
                BrandId = ((p - 1) % BrandCount) + 1,
            });
        }

        var reviewId = 1;
        for (var p = 1; p <= 20; p++)
        {
            db.Reviews.Add(new Review { Id = reviewId++, ProductId = p, Rating = 5, Comment = "Works as described." });
            db.Reviews.Add(new Review { Id = reviewId++, ProductId = p, Rating = 3, Comment = "Arrived late." });
        }

        db.SaveChanges();
    }
}
