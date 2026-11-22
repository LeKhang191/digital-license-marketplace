using Ocelot.DependencyInjection;
using Ocelot.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile(
    "ocelot.json",
    optional: false,
    reloadOnChange: true
);

// Cho phép frontend (Vite dev server) gọi API từ trình duyệt.
// Thêm origin khác bằng biến môi trường Cors__Origins__0, Cors__Origins__1...
var origins = builder.Configuration
    .GetSection("Cors:Origins")
    .Get<string[]>()
    ?? new[] { "http://localhost:5173" };

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(origins)
        .AllowAnyHeader()
        .AllowAnyMethod()));

builder.Services.AddOcelot(builder.Configuration);

var app = builder.Build();

// CORS phải chạy trước Ocelot để xử lý được request preflight (OPTIONS)
app.UseCors();

app.MapGet("/health", () => Results.Ok(
    new { status = "ok", service = "gateway" }
));

await app.UseOcelot();
app.Run();
