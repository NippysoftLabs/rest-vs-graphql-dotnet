using Microsoft.EntityFrameworkCore;

namespace Store.Api.Data;

public sealed class StoreDbContext(DbContextOptions<StoreDbContext> options) : DbContext(options)
{
    public const int MaxCommentLength = 500;

    public DbSet<Brand> Brands => Set<Brand>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<Review> Reviews => Set<Review>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Brand>(brand =>
        {
            brand.Property(b => b.Name).HasMaxLength(100);
            brand.Property(b => b.Country).HasMaxLength(60);
        });

        modelBuilder.Entity<Product>(product =>
        {
            product.Property(p => p.Name).HasMaxLength(120);
            product.Property(p => p.Description).HasMaxLength(1000);
            product.HasOne<Brand>().WithMany().HasForeignKey(p => p.BrandId);
            product.HasIndex(p => p.BrandId);
        });

        modelBuilder.Entity<Review>(review =>
        {
            review.Property(r => r.Comment).HasMaxLength(MaxCommentLength);
            review.HasOne<Product>().WithMany().HasForeignKey(r => r.ProductId);
            review.HasIndex(r => r.ProductId);
        });
    }
}
