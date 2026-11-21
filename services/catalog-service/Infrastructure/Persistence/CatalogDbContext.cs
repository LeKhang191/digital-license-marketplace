using Catalog.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Api.Infrastructure.Persistence;

public sealed class CatalogDbContext(
    DbContextOptions<CatalogDbContext> options
) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var product = modelBuilder.Entity<Product>();

        product.ToTable("products");
        product.HasKey(p => p.Id);
        product.HasIndex(p => p.Slug).IsUnique();

        product.Property(p => p.Name)
            .HasMaxLength(160).IsRequired();

        product.Property(p => p.Slug)
            .HasMaxLength(180).IsRequired();

        product.Property(p => p.Description)
            .HasMaxLength(2000);

        product.Property(p => p.Category)
            .HasMaxLength(80).IsRequired();

        product.Property(p => p.Price)
            .HasPrecision(12, 2);
    }
}