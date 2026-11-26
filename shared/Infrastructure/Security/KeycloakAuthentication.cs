using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Marketplace.Shared.Security;

internal static class KeycloakAuthentication
{
    internal static void Configure(
        IServiceCollection services,
        IConfiguration config)
    {
        var metadata = config["Keycloak:MetadataAddress"]
            ?? throw new InvalidOperationException(
                "Keycloak:MetadataAddress is missing");

        var issuer = config["Keycloak:Issuer"]
            ?? throw new InvalidOperationException(
                "Keycloak:Issuer is missing");

        services
            .AddAuthentication(
                JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MetadataAddress = metadata;
                options.RequireHttpsMetadata = false;
                options.MapInboundClaims = false;

                options.TokenValidationParameters =
                    new TokenValidationParameters
                    {
                        ValidIssuer = issuer,
                        ValidateAudience = false,
                        NameClaimType = "preferred_username",
                        RoleClaimType = ClaimTypes.Role
                    };

                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        AddRealmRoles(context.Principal);

                        return Task.CompletedTask;
                    }
                };
            });

    }

    private static void AddRealmRoles(
        ClaimsPrincipal? principal)
    {
        if (principal?.Identity is not ClaimsIdentity identity)
            return;

        var raw = identity.FindFirst("realm_access")?.Value;

        if (string.IsNullOrEmpty(raw))
            return;

        using var doc = JsonDocument.Parse(raw);

        if (!doc.RootElement.TryGetProperty(
            "roles", out var roles) ||
            roles.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var role in roles.EnumerateArray())
        {
            if (role.ValueKind != JsonValueKind.String)
                continue;

            var value = role.GetString();

            if (!string.IsNullOrEmpty(value))
            {
                identity.AddClaim(
                    new Claim(ClaimTypes.Role, value));
            }
        }
    }
}
