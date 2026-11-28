using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace Marketplace.Shared.Security;

internal static class KeycloakAuthentication
{
    internal const string PartnerClaimName = "partner_id";
    internal const string PartnerRoleName = "partner_api";
    internal const string PartnerPolicyName = "PartnerApiOnly";
    internal const string PartnerSubjectPrefix = "partner:";

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

    internal static void ConfigurePartnerPolicy(
        AuthorizationOptions options)
    {
        options.AddPolicy(PartnerPolicyName, policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireRole(PartnerRoleName);
            policy.RequireAssertion(context =>
                TryGetPartnerId(context.User, out _) &&
                !context.User.IsInRole("customer") &&
                !context.User.IsInRole("catalog_manager") &&
                !context.User.IsInRole("license_coordinator") &&
                !context.User.IsInRole("transaction_auditor") &&
                !context.User.IsInRole("admin"));
        });
    }

    internal static bool IsValidPartnerId(string? partnerId)
    {
        if (string.IsNullOrEmpty(partnerId) ||
            partnerId.Length < 3 ||
            partnerId.Length > 48)
        {
            return false;
        }

        var first = partnerId[0];
        if (!((first >= 'a' && first <= 'z') ||
              (first >= '0' && first <= '9')))
        {
            return false;
        }

        foreach (var ch in partnerId)
        {
            var valid =
                (ch >= 'a' && ch <= 'z') ||
                (ch >= '0' && ch <= '9') ||
                ch is '_' or '-';

            if (!valid)
                return false;
        }

        return true;
    }

    internal static bool TryGetPartnerId(
        ClaimsPrincipal? principal,
        out string partnerId)
    {
        partnerId = "";
        var value = principal?.FindFirst(PartnerClaimName)?.Value;
        if (!IsValidPartnerId(value))
            return false;

        partnerId = value!;
        return true;
    }

    internal static string ToPartnerSubject(string partnerId)
    {
        if (!IsValidPartnerId(partnerId))
            throw new ArgumentException("Invalid partner ID.", nameof(partnerId));

        return PartnerSubjectPrefix + partnerId;
    }

    internal static bool TryExtractPartnerIdFromSubject(
        string? subject,
        out string partnerId)
    {
        partnerId = "";
        if (string.IsNullOrEmpty(subject) ||
            !subject.StartsWith(PartnerSubjectPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var candidate = subject[PartnerSubjectPrefix.Length..];
        if (!IsValidPartnerId(candidate))
            return false;

        partnerId = candidate;
        return true;
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
