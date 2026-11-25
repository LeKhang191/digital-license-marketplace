using Npgsql;

var builder = WebApplication.CreateBuilder(args.Length > 0 && args[0] == "import" ? Array.Empty<string>() : args);
var cs = builder.Configuration.GetConnectionString("LicenseDb")
    ?? throw new InvalidOperationException("LicenseDb required");
var key = Convert.FromBase64String(builder.Configuration["LicenseVault:EncryptionKey"] ?? "");
if (key.Length != 32) throw new InvalidOperationException("LicenseVault requires a 32-byte base64 key");
var store = new VaultStore(NpgsqlDataSource.Create(cs), key);
await store.InitializeAsync();
if (args.Length > 0 && args[0] == "import")
{
    if (args.Length != 2 || !Guid.TryParse(args[1], out var product))
        throw new ArgumentException("Usage: import PRODUCT_UUID; one key per stdin line");
    await store.ImportAsync(product);
    return;
}
builder.Services.AddSingleton(store);
builder.Services.AddHostedService<VaultWorker>();
var app = builder.Build();
app.MapGet("/health", () => Results.Ok());
app.Run();
