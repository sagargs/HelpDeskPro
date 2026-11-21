using HelpDeskPro.API.Configuration;
using HelpDeskPro.API.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDeskPro.API.Extensions;

public static class AuthorizationServiceCollectionExtensions
{
    public static IServiceCollection AddHelpDeskAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(
                AuthorizationPolicies.AdminOnly,
                policy => policy.RequireRole(nameof(UserRole.Admin)));
            options.AddPolicy(
                AuthorizationPolicies.AgentOrAdmin,
                policy => policy.RequireRole(nameof(UserRole.Agent), nameof(UserRole.Admin)));
            options.AddPolicy(
                AuthorizationPolicies.EmployeeOnly,
                policy => policy.RequireRole(nameof(UserRole.Employee)));
            options.AddPolicy(
                AuthorizationPolicies.AuthenticatedUser,
                policy => policy.RequireAuthenticatedUser());
        });

        return services;
    }
}
