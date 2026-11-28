using Marketplace.Shared.Security;

namespace Catalog.Api.Infrastructure.Security;

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
                "CatalogManagerOnly",
                policy =>
                {
                    policy.RequireAuthenticatedUser();
                    policy.RequireRole("catalog_manager");
                });

            KeycloakAuthentication.ConfigurePartnerPolicy(options);
        });

        return services;
    }

}
