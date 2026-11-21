using System.Security.Claims;
using System.Text.Json;
using HelpDeskPro.API.Configuration;
using HelpDeskPro.API.Controllers;
using HelpDeskPro.API.Data;
using HelpDeskPro.API.DTOs;
using HelpDeskPro.API.Enums;
using HelpDeskPro.API.Models;
using HelpDeskPro.API.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskPro.Tests;

public class TicketServiceTests
{
    private const int EmployeeId = 1;
    private const int OtherEmployeeId = 2;
    private const int AgentId = 3;
    private const int OtherAgentId = 4;
    private const int AdminId = 5;
    private const int CategoryId = 10;

    [Fact]
    public async Task EmployeeCanCreateTicketOwnedByTheirAuthenticatedIdentity()
    {
        await using var dbContext = await CreateDbContextAsync();
        var service = new TicketService(dbContext);

        var result = await service.CreateAsync(
            CreateRequest(),
            CreatePrincipal(EmployeeId, UserRole.Employee),
            CancellationToken.None);

        Assert.Equal(TicketOperationStatus.Success, result.Status);
        var ticket = await dbContext.Tickets.SingleAsync();
        Assert.Equal(EmployeeId, ticket.CreatedByUserId);
        Assert.Null(ticket.AssignedToUserId);
        Assert.Equal(TicketStatus.Open, ticket.Status);
        Assert.Equal(DateTimeKind.Utc, ticket.CreatedAtUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, ticket.UpdatedAtUtc.Kind);
        Assert.Equal(ticket.CreatedAtUtc, ticket.UpdatedAtUtc);
    }

    [Fact]
    public async Task NewTicketAlwaysStartsOpen()
    {
        await using var dbContext = await CreateDbContextAsync();
        var service = new TicketService(dbContext);

        await service.CreateAsync(
            CreateRequest(),
            CreatePrincipal(EmployeeId, UserRole.Employee),
            CancellationToken.None);

        Assert.Equal(TicketStatus.Open, (await dbContext.Tickets.SingleAsync()).Status);
    }

    [Fact]
    public async Task EmployeeSeesOnlyTicketsTheyCreated()
    {
        await using var dbContext = await CreateDbContextAsync();
        await SeedTicketsAsync(dbContext);
        var service = new TicketService(dbContext);

        var tickets = await service.GetAllAsync(
            CreatePrincipal(EmployeeId, UserRole.Employee),
            CancellationToken.None);

        Assert.Single(tickets);
        Assert.Equal(101, tickets[0].Id);
    }

    [Fact]
    public async Task EmployeeCannotViewAnotherEmployeesTicket()
    {
        await using var dbContext = await CreateDbContextAsync();
        await SeedTicketsAsync(dbContext);
        var service = new TicketService(dbContext);

        var ticket = await service.GetByIdAsync(
            102,
            CreatePrincipal(EmployeeId, UserRole.Employee),
            CancellationToken.None);

        Assert.Null(ticket);
    }

    [Fact]
    public async Task AgentSeesOnlyTicketsAssignedToThem()
    {
        await using var dbContext = await CreateDbContextAsync();
        await SeedTicketsAsync(dbContext);
        var service = new TicketService(dbContext);

        var tickets = await service.GetAllAsync(
            CreatePrincipal(AgentId, UserRole.Agent),
            CancellationToken.None);

        Assert.Single(tickets);
        Assert.Equal(101, tickets[0].Id);
    }

    [Fact]
    public async Task AgentCannotViewAnotherAgentsAssignedTicket()
    {
        await using var dbContext = await CreateDbContextAsync();
        await SeedTicketsAsync(dbContext);
        var service = new TicketService(dbContext);

        var ticket = await service.GetByIdAsync(
            102,
            CreatePrincipal(AgentId, UserRole.Agent),
            CancellationToken.None);

        Assert.Null(ticket);
    }

    [Fact]
    public async Task AdminCanSeeAllTickets()
    {
        await using var dbContext = await CreateDbContextAsync();
        await SeedTicketsAsync(dbContext);
        var service = new TicketService(dbContext);

        var tickets = await service.GetAllAsync(
            CreatePrincipal(AdminId, UserRole.Admin),
            CancellationToken.None);

        Assert.Equal(3, tickets.Count);
    }

    [Fact]
    public async Task EmployeeCannotUpdateAnotherEmployeesTicket()
    {
        await using var dbContext = await CreateDbContextAsync();
        await SeedTicketsAsync(dbContext);
        var service = new TicketService(dbContext);

        var result = await service.UpdateAsync(
            102,
            CreateUpdateRequest(),
            CreatePrincipal(EmployeeId, UserRole.Employee),
            CancellationToken.None);

        Assert.Equal(TicketOperationStatus.NotFound, result.Status);
    }

    [Theory]
    [InlineData(UserRole.Employee)]
    [InlineData(UserRole.Agent)]
    public async Task NonAdminCannotDeleteTicket(UserRole role)
    {
        await using var dbContext = await CreateDbContextAsync();
        await SeedTicketsAsync(dbContext);
        var service = new TicketService(dbContext);
        var userId = role == UserRole.Employee ? EmployeeId : AgentId;

        var result = await service.DeleteAsync(
            101,
            CreatePrincipal(userId, role),
            CancellationToken.None);

        Assert.Equal(TicketOperationStatus.Forbidden, result.Status);
        Assert.True(await dbContext.Tickets.AnyAsync(ticket => ticket.Id == 101));
    }

    [Fact]
    public async Task AdminCanDeleteTicket()
    {
        await using var dbContext = await CreateDbContextAsync();
        await SeedTicketsAsync(dbContext);
        var service = new TicketService(dbContext);

        var result = await service.DeleteAsync(
            101,
            CreatePrincipal(AdminId, UserRole.Admin),
            CancellationToken.None);

        Assert.Equal(TicketOperationStatus.Success, result.Status);
        Assert.False(await dbContext.Tickets.AnyAsync(ticket => ticket.Id == 101));
    }

    [Fact]
    public async Task AdminGetsNotFoundWhenDeletingMissingTicket()
    {
        await using var dbContext = await CreateDbContextAsync();
        var service = new TicketService(dbContext);

        var result = await service.DeleteAsync(
            999,
            CreatePrincipal(AdminId, UserRole.Admin),
            CancellationToken.None);

        Assert.Equal(TicketOperationStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task CreateRejectsInvalidCategoryAndPriority()
    {
        await using var dbContext = await CreateDbContextAsync();
        var service = new TicketService(dbContext);
        var caller = CreatePrincipal(EmployeeId, UserRole.Employee);

        var invalidCategoryResult = await service.CreateAsync(
            CreateRequest(categoryId: 999),
            caller,
            CancellationToken.None);
        var invalidPriorityResult = await service.CreateAsync(
            CreateRequest(priority: (TicketPriority)999),
            caller,
            CancellationToken.None);

        Assert.Equal(TicketOperationStatus.InvalidRequest, invalidCategoryResult.Status);
        Assert.Equal(TicketOperationStatus.InvalidRequest, invalidPriorityResult.Status);
        Assert.Empty(await dbContext.Tickets.ToListAsync());
    }

    [Fact]
    public async Task EmployeeCannotChangeOwnershipOrAssignmentThroughUpdateBody()
    {
        await using var dbContext = await CreateDbContextAsync();
        await SeedTicketsAsync(dbContext);
        var service = new TicketService(dbContext);
        const string body = """
            {
              "title": "Updated title",
              "description": "Updated description",
              "categoryId": 10,
              "priority": 2,
              "createdByUserId": 2,
              "assignedToUserId": 4
            }
            """;
        var request = JsonSerializer.Deserialize<UpdateTicketRequest>(
            body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(request);
        Assert.Null(typeof(UpdateTicketRequest).GetProperty(nameof(Ticket.CreatedByUserId)));
        Assert.Null(typeof(UpdateTicketRequest).GetProperty(nameof(Ticket.AssignedToUserId)));

        var result = await service.UpdateAsync(
            101,
            request,
            CreatePrincipal(EmployeeId, UserRole.Employee),
            CancellationToken.None);

        Assert.Equal(TicketOperationStatus.Success, result.Status);
        var ticket = await dbContext.Tickets.SingleAsync(item => item.Id == 101);
        Assert.Equal(EmployeeId, ticket.CreatedByUserId);
        Assert.Equal(AgentId, ticket.AssignedToUserId);
    }

    [Fact]
    public async Task EmployeeCannotChangeTicketStatus()
    {
        await using var dbContext = await CreateDbContextAsync();
        await SeedTicketsAsync(dbContext);
        var service = new TicketService(dbContext);
        var request = new UpdateTicketRequest
        {
            Title = "Updated title",
            Description = "Updated description",
            CategoryId = CategoryId,
            Priority = TicketPriority.High,
            Status = TicketStatus.Closed
        };

        var result = await service.UpdateAsync(
            101,
            request,
            CreatePrincipal(EmployeeId, UserRole.Employee),
            CancellationToken.None);

        Assert.Equal(TicketOperationStatus.Forbidden, result.Status);
        Assert.Equal(TicketStatus.Open, (await dbContext.Tickets.SingleAsync(item => item.Id == 101)).Status);
    }

    [Fact]
    public async Task AgentCannotUpdateTicketAssignedToAnotherAgent()
    {
        await using var dbContext = await CreateDbContextAsync();
        await SeedTicketsAsync(dbContext);
        var service = new TicketService(dbContext);

        var result = await service.UpdateAsync(
            102,
            CreateUpdateRequest(),
            CreatePrincipal(AgentId, UserRole.Agent),
            CancellationToken.None);

        Assert.Equal(TicketOperationStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task UpdateSetsUpdatedAtUtc()
    {
        await using var dbContext = await CreateDbContextAsync();
        await SeedTicketsAsync(dbContext);
        var ticket = await dbContext.Tickets.SingleAsync(item => item.Id == 101);
        ticket.UpdatedAtUtc = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await dbContext.SaveChangesAsync();
        var service = new TicketService(dbContext);

        var result = await service.UpdateAsync(
            101,
            CreateUpdateRequest(),
            CreatePrincipal(EmployeeId, UserRole.Employee),
            CancellationToken.None);

        Assert.Equal(TicketOperationStatus.Success, result.Status);
        var updatedTicket = await dbContext.Tickets.SingleAsync(item => item.Id == 101);
        Assert.True(updatedTicket.UpdatedAtUtc > new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(DateTimeKind.Utc, updatedTicket.UpdatedAtUtc.Kind);
    }

    [Fact]
    public void TicketControllerRequiresAuthenticationAndRestrictsDeleteToAdmin()
    {
        var controllerAuthorization = typeof(TicketsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Single();
        var deleteAuthorization = typeof(TicketsController)
            .GetMethod(nameof(TicketsController.Delete))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Single();

        Assert.Equal(AuthorizationPolicies.AuthenticatedUser, controllerAuthorization.Policy);
        Assert.Equal(AuthorizationPolicies.AdminOnly, deleteAuthorization.Policy);
    }

    private static async Task<ApplicationDbContext> CreateDbContextAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var dbContext = new ApplicationDbContext(options);
        dbContext.Users.AddRange(
            CreateUser(EmployeeId, UserRole.Employee),
            CreateUser(OtherEmployeeId, UserRole.Employee),
            CreateUser(AgentId, UserRole.Agent),
            CreateUser(OtherAgentId, UserRole.Agent),
            CreateUser(AdminId, UserRole.Admin));
        dbContext.Categories.Add(new Category
        {
            Id = CategoryId,
            Name = "Hardware",
            Description = "Hardware support",
            IsActive = true
        });
        await dbContext.SaveChangesAsync();
        return dbContext;
    }

    private static async Task SeedTicketsAsync(ApplicationDbContext dbContext)
    {
        dbContext.Tickets.AddRange(
            CreateTicket(101, EmployeeId, AgentId),
            CreateTicket(102, OtherEmployeeId, OtherAgentId),
            CreateTicket(103, OtherEmployeeId, null));
        await dbContext.SaveChangesAsync();
    }

    private static User CreateUser(int id, UserRole role) =>
        new()
        {
            Id = id,
            FullName = $"User {id}",
            Email = $"user{id}@example.com",
            PasswordHash = "test-hash",
            Role = role,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

    private static Ticket CreateTicket(int id, int createdByUserId, int? assignedToUserId) =>
        new()
        {
            Id = id,
            Title = $"Ticket {id}",
            Description = "Ticket description",
            CategoryId = CategoryId,
            CreatedByUserId = createdByUserId,
            AssignedToUserId = assignedToUserId,
            Priority = TicketPriority.Medium,
            Status = TicketStatus.Open,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

    private static ClaimsPrincipal CreatePrincipal(int userId, UserRole role)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim("sub", userId.ToString()),
                new Claim(JwtOptions.RoleClaimType, role.ToString())
            ],
            JwtBearerDefaults.AuthenticationScheme,
            "sub",
            JwtOptions.RoleClaimType);

        return new ClaimsPrincipal(identity);
    }

    private static CreateTicketRequest CreateRequest(
        int categoryId = CategoryId,
        TicketPriority priority = TicketPriority.High) =>
        new()
        {
            Title = "New ticket",
            Description = "A description",
            CategoryId = categoryId,
            Priority = priority
        };

    private static UpdateTicketRequest CreateUpdateRequest() =>
        new()
        {
            Title = "Updated ticket",
            Description = "Updated description",
            CategoryId = CategoryId,
            Priority = TicketPriority.High
        };
}
