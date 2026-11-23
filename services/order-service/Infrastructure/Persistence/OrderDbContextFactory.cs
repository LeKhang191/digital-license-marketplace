using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OrderService.Infrastructure.Persistence;

// Used only by dotnet-ef at design time. No live database is required to scaffold.
public sealed class OrderDbContextFactory : IDesignTimeDbContextFactory<OrderDbContext>
{
    public OrderDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("EF_MIGRATIONS_CONNECTION")
            ?? "Host=localhost;Database=ef_design_only;Username=app;Password=not_used";
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseNpgsql(connection)
            .Options;
        return new OrderDbContext(options);
    }
}
