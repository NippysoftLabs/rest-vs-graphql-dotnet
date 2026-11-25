namespace Store.Api.Data;

// Entities have no navigation properties on purpose: every relationship is loaded
// explicitly, so the number of SQL commands per request is easy to see and count.

public sealed class Brand
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public required string Country { get; set; }
}

public sealed class Product
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public required string Description { get; set; }

    public decimal Price { get; set; }

    public int BrandId { get; set; }
}

public sealed class Review
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public int Rating { get; set; }

    public required string Comment { get; set; }
}
