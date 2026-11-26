using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace OrderService.Infrastructure.Security;

public static class KeycloakAuthExtensions
{
    public static IServiceCollection AddKeycloakAuth(
        this IServiceCollection services,
        IConfiguration config,
        IHostEnvironment env)
    {
        var metadata = config["Keycloak:MetadataAddress"]
            ?? throw new InvalidOperationException(
                "Keycloak:MetadataAddress is missing");

        var issuer = config["Keycloak:Issuer"]
            ?? throw new InvalidOperationException(
                "Keycloak:Issuer is missing");

        var requireHttps = config.GetValue("Keycloak:RequireHttpsMetadata", !env.IsDevelopment());
        var audience = config["Keycloak:Audience"];
        var allowedClient = config["Keycloak:AllowedClientId"] ?? "marketplace-web";

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MetadataAddress = metadata;
                options.RequireHttpsMetadata = requireHttps;
                options.MapInboundClaims = false;

                options.TokenValidationParameters =
                    new TokenValidationParameters
                    {
                        ValidIssuer = issuer,
                        ValidateAudience = !string.IsNullOrWhiteSpace(audience),
                        ValidAudience = audience,
                        NameClaimType = "preferred_username",
                        RoleClaimType = ClaimTypes.Role
                    };

                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = ctx =>
                    {
                        if (!string.IsNullOrWhiteSpace(allowedClient) &&
                            !string.Equals(ctx.Principal?.FindFirst("azp")?.Value, allowedClient, StringComparison.Ordinal))
                        {
                            ctx.Fail("Invalid authorized party");
                            return Task.CompletedTask;
                        }
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
