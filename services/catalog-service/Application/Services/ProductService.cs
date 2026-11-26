using Catalog.Api.Application.DTOs;
using Catalog.Api.Application.Interfaces;
using Catalog.Api.Domain.Entities;
using Catalog.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Catalog.Api.Application.Services;

public sealed class ProductService(
    CatalogDbContext db
) : IProductService
{
    public async Task<PagedProducts> ListAsync(
        int page,
        int pageSize,
        string? search,
        string? category,
        CancellationToken ct)
    {
        var query = db.Products
            .AsNoTracking()
            .Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim()
                .Replace(@"\", @"\\")
                .Replace("%", @"\%")
                .Replace("_", @"\_");

            query = query.Where(p =>
                EF.Functions.ILike(p.Name, $"%{term}%", @"\"));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            var cat = category.Trim();
            query = query.Where(p => p.Category == cat);
        }

        var total = await query.CountAsync(ct);

        var entities = await query
            .OrderBy(p => p.Name)
            .ThenBy(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = entities
            .Select(ProductResponse.From)
            .ToList();

        return new PagedProducts(items, page, pageSize, total);
    }

    public async Task<ProductResponse?> GetAsync(
        Guid id,
        CancellationToken ct)
    {
        var product = await db.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(
                p => p.Id == id && p.IsActive, ct);

        return product is null
            ? null
            : ProductResponse.From(product);
    }

    public async Task<(ProductResponse?, bool)> CreateAsync(
        ProductUpsertRequest request,
        CancellationToken ct)
    {
        var product = new Product();
        Apply(product, request);

        db.Products.Add(product);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
            when (IsUniqueViolation(ex))
        {
            return (null, true);
        }

        return (ProductResponse.From(product), false);
    }

    public async Task<(ProductResponse?, bool)> UpdateAsync(
        Guid id,
        ProductUpsertRequest request,
        CancellationToken ct)
    {
        var product = await db.Products
            .FindAsync([id], ct);

        if (product is null)
            return (null, false);

        Apply(product, request);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
            when (IsUniqueViolation(ex))
        {
            return (null, true);
        }

        return (ProductResponse.From(product), false);
    }

    public async Task<bool> DeleteAsync(
        Guid id,
        CancellationToken ct)
    {
        var product = await db.Products
            .FindAsync([id], ct);

        if (product is null)
            return false;

        db.Products.Remove(product);

        await db.SaveChangesAsync(ct);
        return true;
    }

    private static void Apply(
        Product product,
        ProductUpsertRequest request)
    {
        if (request.ImportedMetadata is not null)
            product.SteamMetadataJson = System.Text.Json.JsonSerializer.Serialize(request.ImportedMetadata);
        product.Name = request.Name.Trim();
        product.Slug = request.Slug.Trim();
        product.Description = request.Description.Trim();
        product.Category = request.Category.Trim();
        product.Price = request.Price;
        product.IsActive = request.IsActive;
        product.UpdatedAtUtc = DateTime.UtcNow;
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        return ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        };
    }
}