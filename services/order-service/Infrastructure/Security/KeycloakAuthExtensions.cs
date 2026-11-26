using Marketplace.Shared.Security;

namespace OrderService.Infrastructure.Security;

public static class KeycloakAuthExtensions
{
    public static IServiceCollection AddKeycloakAuth(
        this IServiceCollection services,
        IConfiguration config)
    {
        KeycloakAuthentication.Configure(services, config);

        services.AddAuthorization(options =>
        {
            // Legacy role compatibility
            options.AddPolicy("AdminOnly", policy =>
                policy.RequireRole("admin"));

            // Only customers can purchase products
            options.AddPolicy("CustomerOnly", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireRole("customer");

                policy.RequireAssertion(context =>
                    !context.User.IsInRole("catalog_manager") &&
                    !context.User.IsInRole("license_coordinator") &&
                    !context.User.IsInRole("transaction_auditor") &&
                    !context.User.IsInRole("admin"));
            });

            // Reserved for transaction management
            options.AddPolicy("TransactionAuditorOnly", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireRole("transaction_auditor");
            });
        });

        return services;
    }

}
