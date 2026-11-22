using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Catalog.Api.Infrastructure.Security;

public static class KeycloakAuthExtensions
{
    public static IServiceCollection AddKeycloakAuth(
        this IServiceCollection services,
        IConfiguration config)
    {
        var metadata = config["Keycloak:MetadataAddress"]
            ?? throw new InvalidOperationException(
                "Keycloak:MetadataAddress is missing");

        var issuer = config["Keycloak:Issuer"]
            ?? throw new InvalidOperationException(
                "Keycloak:Issuer is missing");

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Lấy khóa công khai qua mạng nội bộ Docker (keycloak:8080)
                options.MetadataAddress = metadata;
                options.RequireHttpsMetadata = false;
                options.MapInboundClaims = false;

                options.TokenValidationParameters =
                    new TokenValidationParameters
                    {
                        // Issuer là địa chỉ mà client dùng để đăng nhập
                        ValidIssuer = issuer,
                        ValidateAudience = false,
                        NameClaimType = "preferred_username",
                        RoleClaimType = ClaimTypes.Role
                    };

                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = ctx =>
                    {
                        AddRealmRoles(ctx.Principal);
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("AdminOnly",
                policy => policy.RequireRole("admin"));
        });

        return services;
    }

    // Keycloak để role trong claim "realm_access": {"roles":[...]}
    private static void AddRealmRoles(ClaimsPrincipal? principal)
    {
        if (principal?.Identity is not ClaimsIdentity identity) return;

        var raw = identity.FindFirst("realm_access")?.Value;
        if (string.IsNullOrEmpty(raw)) return;

        using var doc = JsonDocument.Parse(raw);

        if (!doc.RootElement.TryGetProperty("roles", out var roles) ||
            roles.ValueKind != JsonValueKind.Array) return;

        foreach (var role in roles.EnumerateArray())
        {
            var value = role.GetString();

            if (!string.IsNullOrEmpty(value))
                identity.AddClaim(new Claim(ClaimTypes.Role, value));
        }
    }
}
