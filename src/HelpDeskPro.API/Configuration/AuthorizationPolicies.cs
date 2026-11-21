namespace HelpDeskPro.API.Configuration;

public static class AuthorizationPolicies
{
    public const string AdminOnly = nameof(AdminOnly);

    public const string AgentOrAdmin = nameof(AgentOrAdmin);

    public const string EmployeeOnly = nameof(EmployeeOnly);

    public const string AuthenticatedUser = nameof(AuthenticatedUser);
}
