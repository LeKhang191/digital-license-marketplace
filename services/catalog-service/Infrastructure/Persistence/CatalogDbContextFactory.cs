using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Catalog.Api.Infrastructure.Persistence;

// Used only by dotnet-ef at design time. No live database is required to scaffold.
public sealed class CatalogDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("EF_MIGRATIONS_CONNECTION")
            ?? "Host=localhost;Database=ef_design_only;Username=app;Password=not_used";
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(connection)
            .Options;
        return new CatalogDbContext(options);
    }
}
