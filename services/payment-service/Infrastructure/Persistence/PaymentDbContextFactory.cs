using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PaymentService.Infrastructure.Persistence;

public sealed class PaymentDbContextFactory :
    IDesignTimeDbContextFactory<PaymentDbContext>
{
    public PaymentDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable(
            "EF_MIGRATIONS_CONNECTION")
            ?? throw new InvalidOperationException(
                "EF_MIGRATIONS_CONNECTION is required");

        var options =
            new DbContextOptionsBuilder<PaymentDbContext>();

        options.UseNpgsql(connection);

        return new PaymentDbContext(options.Options);
    }
}
