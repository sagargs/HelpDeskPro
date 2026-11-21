using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using HelpDeskPro.API.Configuration;
using HelpDeskPro.API.Data;
using HelpDeskPro.API.DTOs;
using HelpDeskPro.API.Enums;
using HelpDeskPro.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace HelpDeskPro.Tests;

public class AuthServiceTests
{
    private const string TestSigningKey = "test-only-signing-key-with-more-than-32-bytes";

    [Fact]
    public async Task RegisterAsync_CreatesEmployeeAndReturnsAuthenticationResponse()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        var response = await service.RegisterAsync(CreateRegisterRequest(), CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal("Taylor Employee", response.FullName);
        Assert.Equal("TAYLOR@EXAMPLE.COM", response.Email);
        Assert.Equal(nameof(UserRole.Employee), response.Role);
        Assert.NotEmpty(response.AccessToken);

        var user = await dbContext.Users.SingleAsync();
        Assert.Equal(UserRole.Employee, user.Role);
        Assert.True(user.IsActive);
    }

    [Fact]
    public async Task RegisterAsync_RejectsDuplicateEmailIgnoringCase()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        var firstResponse = await service.RegisterAsync(CreateRegisterRequest(), CancellationToken.None);
        var duplicateResponse = await service.RegisterAsync(
            CreateRegisterRequest(email: " taylor@example.com "),
            CancellationToken.None);

        Assert.NotNull(firstResponse);
        Assert.Null(duplicateResponse);
        Assert.Equal(1, await dbContext.Users.CountAsync());
    }

    [Fact]
    public async Task RegisterAsync_StoresBcryptHashAndNeverReturnsIt()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        const string password = "StrongPassword123!";

        var response = await service.RegisterAsync(
            CreateRegisterRequest(password: password),
            CancellationToken.None);
        var user = await dbContext.Users.SingleAsync();

        Assert.NotNull(response);
        Assert.NotEqual(password, user.PasswordHash);
        Assert.True(BCrypt.Net.BCrypt.Verify(password, user.PasswordHash));
        Assert.DoesNotContain(user.PasswordHash, System.Text.Json.JsonSerializer.Serialize(response));
    }

    [Fact]
    public async Task RegisterAsync_CannotAssignAdminRoleThroughPublicRequest()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        await service.RegisterAsync(CreateRegisterRequest(), CancellationToken.None);

        var user = await dbContext.Users.SingleAsync();
        Assert.Equal(UserRole.Employee, user.Role);
        Assert.Null(typeof(RegisterRequest).GetProperty("Role"));
    }

    [Fact]
    public async Task LoginAsync_ReturnsTokenForValidCredentials()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        await service.RegisterAsync(CreateRegisterRequest(), CancellationToken.None);

        var response = await service.LoginAsync(
            new LoginRequest { Email = "taylor@example.com", Password = "StrongPassword123!" },
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal(nameof(UserRole.Employee), response.Role);
        Assert.NotEmpty(response.AccessToken);
    }

    [Fact]
    public async Task LoginAsync_RejectsInvalidPassword()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        await service.RegisterAsync(CreateRegisterRequest(), CancellationToken.None);

        var invalidPasswordResponse = await service.LoginAsync(
            new LoginRequest { Email = "taylor@example.com", Password = "WrongPassword123!" },
            CancellationToken.None);
        var unknownEmailResponse = await service.LoginAsync(
            new LoginRequest { Email = "unknown@example.com", Password = "WrongPassword123!" },
            CancellationToken.None);

        Assert.Null(invalidPasswordResponse);
        Assert.Null(unknownEmailResponse);
    }

    [Fact]
    public async Task LoginAsync_RejectsInactiveUser()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        await service.RegisterAsync(CreateRegisterRequest(), CancellationToken.None);
        var user = await dbContext.Users.SingleAsync();
        user.IsActive = false;
        await dbContext.SaveChangesAsync();

        var response = await service.LoginAsync(
            new LoginRequest { Email = "taylor@example.com", Password = "StrongPassword123!" },
            CancellationToken.None);

        Assert.Null(response);
    }

    [Fact]
    public async Task LoginAsync_TokenContainsUserIdEmailAndRoleClaims()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        await service.RegisterAsync(CreateRegisterRequest(), CancellationToken.None);
        var user = await dbContext.Users.SingleAsync();

        var response = await service.LoginAsync(
            new LoginRequest { Email = "taylor@example.com", Password = "StrongPassword123!" },
            CancellationToken.None);

        Assert.NotNull(response);
        var tokenHandler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = tokenHandler.ValidateToken(
            response.AccessToken,
            new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSigningKey)),
                ValidateIssuer = true,
                ValidIssuer = "HelpDeskPro.Tests",
                ValidateAudience = true,
                ValidAudience = "HelpDeskPro.Tests",
                ValidateLifetime = true,
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                ClockSkew = TimeSpan.Zero
            },
            out _);

        Assert.Equal(user.Id.ToString(), principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value);
        Assert.Equal(user.Email, principal.FindFirst(JwtRegisteredClaimNames.Email)?.Value);
        Assert.Equal(nameof(UserRole.Employee), principal.FindFirst("role")?.Value);
    }

    [Fact]
    public void RegisterRequest_RejectsWeakPasswords()
    {
        var request = CreateRegisterRequest(password: "weak");
        var validationResults = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(
            request,
            new ValidationContext(request),
            validationResults,
            validateAllProperties: true);

        Assert.False(isValid);
        Assert.Contains(validationResults, result => result.MemberNames.Contains(nameof(RegisterRequest.Password)));
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static AuthService CreateService(ApplicationDbContext dbContext)
    {
        var options = Options.Create(new JwtOptions
        {
            SigningKey = TestSigningKey,
            Issuer = "HelpDeskPro.Tests",
            Audience = "HelpDeskPro.Tests",
            ExpiryMinutes = 30
        });

        return new AuthService(dbContext, options);
    }

    private static RegisterRequest CreateRegisterRequest(
        string email = "taylor@example.com",
        string password = "StrongPassword123!") =>
        new()
        {
            FullName = "Taylor Employee",
            Email = email,
            Password = password
        };
}
