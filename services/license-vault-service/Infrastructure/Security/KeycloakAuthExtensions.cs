using Marketplace.Shared.Security;

namespace LicenseVault.Api.Infrastructure.Security;

public static class KeycloakAuthExtensions
{
    public static IServiceCollection AddKeycloakAuth(
        this IServiceCollection services,
        IConfiguration config)
    {
        KeycloakAuthentication.Configure(services, config);

        services.AddAuthorization(options =>
        {
            options.AddPolicy(
                "LicenseCoordinatorOnly",
                policy =>
                {
                    policy.RequireAuthenticatedUser();

                    policy.RequireRole(
                        "license_coordinator");
                });

            options.AddPolicy(
                "CustomerOnly",
                policy =>
                {
                    policy.RequireAuthenticatedUser();
                    policy.RequireRole("customer");

                    policy.RequireAssertion(context =>
                        !context.User.IsInRole("catalog_manager") &&
                        !context.User.IsInRole("license_coordinator") &&
                        !context.User.IsInRole("transaction_auditor") &&
                        !context.User.IsInRole("admin"));
                });
        });

        return services;
    }

}
