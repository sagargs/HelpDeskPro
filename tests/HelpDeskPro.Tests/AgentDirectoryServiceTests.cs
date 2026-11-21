using System.Text.Json;
using HelpDeskPro.API.Configuration;
using HelpDeskPro.API.Controllers;
using HelpDeskPro.API.Data;
using HelpDeskPro.API.DTOs;
using HelpDeskPro.API.Enums;
using HelpDeskPro.API.Models;
using HelpDeskPro.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskPro.Tests;

public class AgentDirectoryServiceTests
{
    [Fact]
    public async Task GetActiveAgentsAsyncReturnsOnlyActiveAgentAccounts()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.AddRange(
            CreateUser(1, UserRole.Agent, isActive: true),
            CreateUser(2, UserRole.Agent, isActive: false),
            CreateUser(3, UserRole.Employee, isActive: true),
            CreateUser(4, UserRole.Admin, isActive: true));
        await dbContext.SaveChangesAsync();
        var service = new AgentDirectoryService(dbContext);

        var agents = await service.GetActiveAgentsAsync(CancellationToken.None);

        var agent = Assert.Single(agents);
        Assert.Equal(1, agent.UserId);
        Assert.Equal("User 1", agent.FullName);
        Assert.Equal("user1@example.com", agent.Email);
    }

    [Fact]
    public async Task AgentListResponseDoesNotExposePasswordHashes()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(CreateUser(1, UserRole.Agent, isActive: true));
        await dbContext.SaveChangesAsync();
        var service = new AgentDirectoryService(dbContext);

        var agents = await service.GetActiveAgentsAsync(CancellationToken.None);
        var serializedResponse = JsonSerializer.Serialize(agents);

        Assert.Null(typeof(AgentListItemResponse).GetProperty(nameof(User.PasswordHash)));
        Assert.DoesNotContain("sensitive-test-hash", serializedResponse);
    }

    [Fact]
    public void UsersControllerRequiresAdminPolicy()
    {
        var authorization = typeof(UsersController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Single();

        Assert.Equal(AuthorizationPolicies.AdminOnly, authorization.Policy);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static User CreateUser(int id, UserRole role, bool isActive) =>
        new()
        {
            Id = id,
            FullName = $"User {id}",
            Email = $"user{id}@example.com",
            PasswordHash = "sensitive-test-hash",
            Role = role,
            IsActive = isActive,
            CreatedAtUtc = DateTime.UtcNow
        };
}
