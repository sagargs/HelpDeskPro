using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HelpDeskPro.API.Configuration;
using HelpDeskPro.API.Controllers;
using HelpDeskPro.API.Data;
using HelpDeskPro.API.DTOs;
using HelpDeskPro.API.Enums;
using HelpDeskPro.API.Extensions;
using HelpDeskPro.API.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace HelpDeskPro.Tests;

public class AuthorizationPolicyTests
{
    private const string TestSigningKey = "test-only-signing-key-with-more-than-32-bytes";
    private const string TestIssuer = "HelpDeskPro.AuthorizationTests";
    private const string TestAudience = "HelpDeskPro.AuthorizationTests";

    [Fact]
    public async Task EmployeeSatisfiesAuthenticatedUserAndEmployeeOnly()
    {
        using var provider = CreateAuthorizationProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var employee = CreatePrincipal(UserRole.Employee);

        Assert.True((await authorization.AuthorizeAsync(
            employee, null, AuthorizationPolicies.AuthenticatedUser)).Succeeded);
        Assert.True((await authorization.AuthorizeAsync(
            employee, null, AuthorizationPolicies.EmployeeOnly)).Succeeded);
    }

    [Fact]
    public async Task EmployeeIsRejectedByAdminOnly()
    {
        using var provider = CreateAuthorizationProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();

        var result = await authorization.AuthorizeAsync(
            CreatePrincipal(UserRole.Employee),
            null,
            AuthorizationPolicies.AdminOnly);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task EmployeeIsRejectedByAgentOrAdmin()
    {
        using var provider = CreateAuthorizationProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();

        var result = await authorization.AuthorizeAsync(
            CreatePrincipal(UserRole.Employee),
            null,
            AuthorizationPolicies.AgentOrAdmin);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task AgentSatisfiesAgentOrAdmin()
    {
        using var provider = CreateAuthorizationProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();

        var result = await authorization.AuthorizeAsync(
            CreatePrincipal(UserRole.Agent),
            null,
            AuthorizationPolicies.AgentOrAdmin);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task AgentIsRejectedByAdminOnly()
    {
        using var provider = CreateAuthorizationProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();

        var result = await authorization.AuthorizeAsync(
            CreatePrincipal(UserRole.Agent),
            null,
            AuthorizationPolicies.AdminOnly);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task AdminSatisfiesAdminOnly()
    {
        using var provider = CreateAuthorizationProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();

        var result = await authorization.AuthorizeAsync(
            CreatePrincipal(UserRole.Admin),
            null,
            AuthorizationPolicies.AdminOnly);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task AnonymousUserIsRejectedByAllAuthenticatedPolicies()
    {
        using var provider = CreateAuthorizationProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
        var authenticatedPolicies = new[]
        {
            AuthorizationPolicies.AdminOnly,
            AuthorizationPolicies.AgentOrAdmin,
            AuthorizationPolicies.EmployeeOnly,
            AuthorizationPolicies.AuthenticatedUser
        };

        foreach (var policy in authenticatedPolicies)
        {
            var result = await authorization.AuthorizeAsync(anonymous, null, policy);
            Assert.False(result.Succeeded);
        }
    }

    [Fact]
    public void RegistrationAndLoginActionsAllowAnonymousAccess()
    {
        var registerMethod = typeof(AuthController).GetMethod(nameof(AuthController.Register));
        var loginMethod = typeof(AuthController).GetMethod(nameof(AuthController.Login));

        Assert.Contains(registerMethod!.GetCustomAttributes(inherit: true), attribute => attribute is AllowAnonymousAttribute);
        Assert.Contains(loginMethod!.GetCustomAttributes(inherit: true), attribute => attribute is AllowAnonymousAttribute);
    }

    [Fact]
    public async Task ValidatedJwtRoleIsRecognizedByIdentityAndAuthorizationPolicies()
    {
        await using var dbContext = CreateDbContext();
        var jwtOptions = CreateJwtOptions();
        var authService = new AuthService(dbContext, Options.Create(jwtOptions));
        var authentication = await authService.RegisterAsync(
            new RegisterRequest
            {
                FullName = "Test Employee",
                Email = "employee@example.com",
                Password = "StrongPassword123!"
            },
            CancellationToken.None);

        Assert.NotNull(authentication);

        var bearerOptions = new JwtBearerOptions();
        new JwtBearerOptionsSetup(Options.Create(jwtOptions))
            .Configure(JwtBearerDefaults.AuthenticationScheme, bearerOptions);

        Assert.Equal(JwtOptions.RoleClaimType, bearerOptions.TokenValidationParameters.RoleClaimType);
        var tokenHandler = new JwtSecurityTokenHandler { MapInboundClaims = bearerOptions.MapInboundClaims };
        var principal = tokenHandler.ValidateToken(
            authentication.AccessToken,
            bearerOptions.TokenValidationParameters,
            out _);

        Assert.True(principal.Identity?.IsAuthenticated);
        Assert.True(principal.IsInRole(nameof(UserRole.Employee)));
        Assert.False(principal.IsInRole(nameof(UserRole.Agent)));
        Assert.False(principal.IsInRole(nameof(UserRole.Admin)));

        using var provider = CreateAuthorizationProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        Assert.True((await authorization.AuthorizeAsync(
            principal, null, AuthorizationPolicies.AuthenticatedUser)).Succeeded);
        Assert.True((await authorization.AuthorizeAsync(
            principal, null, AuthorizationPolicies.EmployeeOnly)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(
            principal, null, AuthorizationPolicies.AgentOrAdmin)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(
            principal, null, AuthorizationPolicies.AdminOnly)).Succeeded);
    }

    [Fact]
    public void BearerValidationRejectsInvalidSignatureAndExpiredToken()
    {
        var jwtOptions = CreateJwtOptions();
        var bearerOptions = new JwtBearerOptions();
        new JwtBearerOptionsSetup(Options.Create(jwtOptions))
            .Configure(JwtBearerDefaults.AuthenticationScheme, bearerOptions);
        var tokenHandler = new JwtSecurityTokenHandler();
        var invalidSignatureToken = CreateToken(
            "different-test-signing-key-with-more-than-32-bytes",
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddMinutes(10));
        var expiredToken = CreateToken(
            TestSigningKey,
            DateTime.UtcNow.AddHours(-2),
            DateTime.UtcNow.AddHours(-1));

        Assert.ThrowsAny<SecurityTokenException>(() =>
            tokenHandler.ValidateToken(
                invalidSignatureToken,
                bearerOptions.TokenValidationParameters,
                out _));
        Assert.Throws<SecurityTokenExpiredException>(() =>
            tokenHandler.ValidateToken(
                expiredToken,
                bearerOptions.TokenValidationParameters,
                out _));
    }

    private static ServiceProvider CreateAuthorizationProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHelpDeskAuthorization();
        return services.BuildServiceProvider();
    }

    private static ClaimsPrincipal CreatePrincipal(UserRole role)
    {
        var identity = new ClaimsIdentity(
            [new Claim(JwtOptions.RoleClaimType, role.ToString())],
            JwtBearerDefaults.AuthenticationScheme,
            JwtRegisteredClaimNames.Sub,
            JwtOptions.RoleClaimType);

        return new ClaimsPrincipal(identity);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static JwtOptions CreateJwtOptions() =>
        new()
        {
            SigningKey = TestSigningKey,
            Issuer = TestIssuer,
            Audience = TestAudience,
            ExpiryMinutes = 30
        };

    private static string CreateToken(string signingKey, DateTime notBefore, DateTime expires)
    {
        var token = new JwtSecurityToken(
            issuer: TestIssuer,
            audience: TestAudience,
            claims: [new Claim(JwtOptions.RoleClaimType, nameof(UserRole.Employee))],
            notBefore: notBefore,
            expires: expires,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
