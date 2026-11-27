using Microsoft.EntityFrameworkCore;
using SupportService.Infrastructure.Persistence;
using SupportService.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration
    .GetConnectionString("SupportDb")
    ?? throw new InvalidOperationException(
        "SupportDb connection string is missing");

builder.Services.AddDbContext<SupportDbContext>(
    options => options.UseNpgsql(connectionString));

foreach (var endpoint in new[]
{
    (
        Name: "SupportOrders",
        Url: builder.Configuration["Services:OrderUrl"]
             ?? "http://order:8080"
    ),
    (
        Name: "SupportLicenses",
        Url: builder.Configuration["Services:LicenseUrl"]
             ?? "http://license-vault:8080"
    )
})
{
    builder.Services.AddHttpClient(
        endpoint.Name, client =>
        {
            client.BaseAddress =
                new Uri(endpoint.Url.TrimEnd('/') + "/");

            client.Timeout = TimeSpan.FromSeconds(5);
        })
        .ConfigurePrimaryHttpMessageHandler(() =>
            new HttpClientHandler
            {
                AllowAutoRedirect = false
            });
}

builder.Services.AddKeycloakAuth(
    builder.Configuration);

builder.Services.AddHttpClient("Jira", client =>
    client.Timeout = TimeSpan.FromSeconds(15))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false
    });
builder.Services.AddScoped<SupportService.Infrastructure.Integrations.JiraClient>();

builder.Services.AddControllers();
builder.Services.AddHealthChecks();

builder.Logging.AddFilter(
    "Microsoft.EntityFrameworkCore.Database.Command",
    LogLevel.Warning);

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
