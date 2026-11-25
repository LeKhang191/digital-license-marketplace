
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LicenseVault.Api.Infrastructure.Persistence;

public sealed class LicenseDbContextFactory
    : IDesignTimeDbContextFactory<LicenseDbContext>
{
    public LicenseDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable(
                "EF_MIGRATIONS_CONNECTION")
            ?? throw new InvalidOperationException(
                "EF_MIGRATIONS_CONNECTION is required.");

        var options = new DbContextOptionsBuilder<LicenseDbContext>();

        options.UseNpgsql(connectionString);

        return new LicenseDbContext(options.Options);
    }
}
