using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
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

public class TicketWorkflowTests
{
    private const int EmployeeId = 1;
    private const int OtherEmployeeId = 2;
    private const int AgentId = 3;
    private const int OtherAgentId = 4;
    private const int AdminId = 5;
    private const int CategoryId = 10;

    [Theory]
    [InlineData(TicketStatus.Open, TicketStatus.InProgress)]
    [InlineData(TicketStatus.InProgress, TicketStatus.Resolved)]
    [InlineData(TicketStatus.Resolved, TicketStatus.InProgress)]
    [InlineData(TicketStatus.Resolved, TicketStatus.Closed)]
    public async Task AdminCanCompleteEachAllowedStatusTransition(
        TicketStatus currentStatus,
        TicketStatus nextStatus)
    {
        await using var dbContext = await CreateDbContextAsync();
        await SeedTicketsAsync(dbContext);
        await SetTicketStatusAsync(dbContext, 101, currentStatus);
        var service = new TicketService(dbContext, new SlaService(TimeProvider.System));

        var result = await service.UpdateStatusAsync(
            101,
            new UpdateTicketStatusRequest { Status = nextStatus },
            CreatePrincipal(AdminId, UserRole.Admin),
            CancellationToken.None);

        Assert.Equal(TicketOperationStatus.Success, result.Status);
        Assert.Equal(nextStatus, result.Ticket?.Status);
        var history = await dbContext.TicketHistories.SingleAsync();
        Assert.Equal(AdminId, history.ChangedByUserId);
        Assert.Equal(currentStatus, history.OldStatus);
        Assert.Equal(nextStatus, history.NewStatus);
        Assert.Contains(currentStatus.ToString(), history.Action);
        Assert.Contains(nextStatus.ToString(), history.Action);
        Assert.Equal(DateTimeKind.Utc, history.ChangedAtUtc.Kind);
    }

    [Theory]
    [InlineData(TicketStatus.Open, TicketStatus.Open)]
    [InlineData(TicketStatus.Open, TicketStatus.Resolved)]
    [InlineData(TicketStatus.Open, TicketStatus.Closed)]
    [InlineData(TicketStatus.InProgress, TicketStatus.Open)]
    [InlineData(TicketStatus.InProgress, TicketStatus.InProgress)]
    [InlineData(TicketStatus.InProgress, TicketStatus.Closed)]
    [InlineData(TicketStatus.Resolved, TicketStatus.Open)]
    [InlineData(TicketStatus.Resolved, TicketStatus.Resolved)]
    [InlineData(TicketStatus.Closed, TicketStatus.Open)]
    [InlineData(TicketStatus.Closed, TicketStatus.InProgress)]
    [InlineData(TicketStatus.Closed, TicketStatus.Resolved)]
    [InlineData(TicketStatus.Closed, TicketStatus.Closed)]
    public async Task AdminCannotCompleteDisallowedStatusTransition(
        TicketStatus currentStatus,
        TicketStatus nextStatus)
    {
        await using var dbContext = await CreateDbContextAsync();
        await SeedTicketsAsync(dbContext);
        await SetTicketStatusAsync(dbContext, 101, currentStatus);
        var service = new TicketService(dbContext, new SlaService(TimeProvider.System));

        var result = await service.UpdateStatusAsync(
            101,
            new UpdateTicketStatusRequest { Status = nextStatus },
            CreatePrincipal(AdminId, UserRole.Admin),
            CancellationToken.None);

        Assert.Equal(TicketOperationStatus.InvalidRequest, result.Status);
        Assert.Equal(currentStatus, (await dbContext.Tickets.SingleAsync(item => item.Id == 101)).Status);
        Assert.Empty(await dbContext.TicketHistories.ToListAsync());
    }

    [Fact]
    public async Task AssignedAgentCanChangeStatus()
    {
        await using var dbContext = await CreateDbContextAsync();
        await SeedTicketsAsync(dbContext);
        var service = new TicketService(dbContext, new SlaService(TimeProvider.System));

        var result = await service.UpdateStatusAsync(
            101,
            new UpdateTicketStatusRequest { Status = TicketStatus.InProgress },
            CreatePrincipal(AgentId, UserRole.Agent),
            CancellationToken.None);

        Assert.Equal(TicketOperationStatus.Success, result.Status);
        Assert.Equal(AgentId, (await dbContext.TicketHistories.SingleAsync()).ChangedByUserId);
    }

    [Fact]
    public async Task EmployeeCannotChangeStatus()
    {
        await using var dbContext = await CreateDbContextAsync();
        await SeedTicketsAsync(dbContext);
        var service = new TicketService(dbContext, new SlaService(TimeProvider.System));

        var result = await service.UpdateStatusAsync(
            101,
            new UpdateTicketStatusRequest { Status = TicketStatus.InProgress },
            CreatePrincipal(EmployeeId, UserRole.Employee),
            CancellationToken.None);

        Assert.Equal(TicketOperationStatus.Forbidden, result.Status);
        Assert.Equal(TicketStatus.Open, (await dbContext.Tickets.SingleAsync(item => item.Id == 101)).Status);
        Assert.Empty(await dbContext.TicketHistories.ToListAsync());
    }

    [Fact]
    public async Task UnassignedAgentCannotChangeStatus()
    {
        await using var dbContext = await CreateDbContextAsync();
        await SeedTicketsAsync(dbContext);
        var service = new TicketService(dbContext, new SlaService(TimeProvider.System));

        var result = await service.UpdateStatusAsync(
            103,
            new UpdateTicketStatusRequest { Status = TicketStatus.InProgress },
            CreatePrincipal(AgentId, UserRole.Agent),
            CancellationToken.None);

        Assert.Equal(TicketOperationStatus.NotFound, result.Status);
        Assert.Empty(await dbContext.TicketHistories.ToListAsync());
    }

    [Fact]
    public async Task AgentCannotChangeStatusOfAnotherAgentsTicket()
    {
        await using var dbContext = await CreateDbContextAsync();
        await SeedTicketsAsync(dbContext);
        var service = new TicketService(dbContext, new SlaService(TimeProvider.System));

        var result = await service.UpdateStatusAsync(
            102,
            new UpdateTicketStatusRequest { Status = TicketStatus.InProgress },
            CreatePrincipal(AgentId, UserRole.Agent),
            CancellationToken.None);

        Assert.Equal(TicketOperationStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task NonexistentTicketCannotChangeStatus()
    {
        await using var dbContext = await CreateDbContextAsync();
        var service = new TicketService(dbContext, new SlaService(TimeProvider.System));

        var result = await service.UpdateStatusAsync(
            999,
            new UpdateTicketStatusRequest { Status = TicketStatus.InProgress },
            CreatePrincipal(AdminId, UserRole.Admin),
            CancellationToken.None);

        Assert.Equal(TicketOperationStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task InvalidStatusValueIsRejected()
    {
        await using var dbContext = await CreateDbContextAsync();
        await SeedTicketsAsync(dbContext);
        var service = new TicketService(dbContext, new SlaService(TimeProvider.System));

        var result = await service.UpdateStatusAsync(
            101,
            new UpdateTicketStatusRequest { Status = (TicketStatus)999 },
            CreatePrincipal(AdminId, UserRole.Admin),
            CancellationToken.None);

        Assert.Equal(TicketOperationStatus.InvalidRequest, result.Status);
        Assert.Empty(await dbContext.TicketHistories.ToListAsync());
    }

    [Fact]
    public async Task AssignmentChangesAreAuditedOnceAndNoOpAssignmentIsNotDuplicated()
    {
        await using var dbContext = await CreateDbContextAsync();
        await SeedTicketsAsync(dbContext);
        var service = new TicketService(dbContext, new SlaService(TimeProvider.System));
        var admin = CreatePrincipal(AdminId, UserRole.Admin);

        await service.AssignAsync(
            103,
            new AssignTicketRequest { AgentUserId = AgentId },
            admin,
            CancellationToken.None);
        await service.AssignAsync(
            103,
            new AssignTicketRequest { AgentUserId = AgentId },
            admin,
            CancellationToken.None);
        await service.AssignAsync(
            103,
            new AssignTicketRequest { AgentUserId = OtherAgentId },
            admin,
            CancellationToken.None);
        await service.AssignAsync(
            103,
            new AssignTicketRequest { AgentUserId = null },
            admin,
            CancellationToken.None);

        var history = await dbContext.TicketHistories
            .OrderBy(item => item.Id)
            .ToListAsync();
        Assert.Equal(3, history.Count);
        Assert.Equal(AdminId, history[0].ChangedByUserId);
        Assert.Contains($"Agent user {AgentId}", history[0].Action);
        Assert.Contains($"Agent user {AgentId}", history[1].Action);
        Assert.Contains($"Agent user {OtherAgentId}", history[1].Action);
        Assert.Contains($"Agent user {OtherAgentId}", history[2].Action);
        Assert.All(history, item => Assert.Equal(DateTimeKind.Utc, item.ChangedAtUtc.Kind));
    }

    [Fact]
    public async Task MeaningfulTicketUpdateIsRecordedInHistory()
    {
        await using var dbContext = await CreateDbContextAsync();
        await SeedTicketsAsync(dbContext);
        var service = new TicketService(dbContext, new SlaService(TimeProvider.System));

        var result = await service.UpdateAsync(
            101,
            new UpdateTicketRequest
            {
                Title = "A revised title",
                Description = "A revised description",
                CategoryId = CategoryId,
                Priority = TicketPriority.High
            },
            CreatePrincipal(EmployeeId, UserRole.Employee),
            CancellationToken.None);

        Assert.Equal(TicketOperationStatus.Success, result.Status);
        var history = await dbContext.TicketHistories.SingleAsync();
        Assert.Equal(EmployeeId, history.ChangedByUserId);
        Assert.Null(history.OldStatus);
        Assert.Null(history.NewStatus);
        Assert.Contains(nameof(Ticket.Title), history.Action);
        Assert.Contains(nameof(Ticket.Description), history.Action);
        Assert.Contains(nameof(Ticket.Priority), history.Action);
    }

    [Fact]
    public async Task CreatorCanAddAndViewCommentWithClaimDerivedAuthor()
    {
        await using var dbContext = await CreateDbContextAsync();
        await SeedTicketsAsync(dbContext);
        var service = new TicketService(dbContext, new SlaService(TimeProvider.System));
        var caller = CreatePrincipal(EmployeeId, UserRole.Employee);

        var created = await service.CreateCommentAsync(
            101,
            new CreateTicketCommentRequest { CommentText = "  Please investigate.  " },
            caller,
            CancellationToken.None);
        var comments = await service.GetCommentsAsync(101, caller, CancellationToken.None);

        Assert.Equal(TicketOperationStatus.Success, created.Status);
        Assert.Equal(EmployeeId, created.Comment?.AuthorUserId);
        Assert.Equal("User 1", created.Comment?.AuthorFullName);
        Assert.Equal("Please investigate.", created.Comment?.CommentText);
        Assert.Equal(TimeSpan.Zero, created.Comment?.CreatedAtUtc.Offset);
        Assert.Null(typeof(CreateTicketCommentRequest).GetProperty(nameof(TicketComment.UserId)));
        Assert.Null(typeof(TicketCommentResponse).GetProperty(nameof(User.PasswordHash)));
        Assert.Single(comments!);
        Assert.Equal(EmployeeId, (await dbContext.TicketComments.SingleAsync()).UserId);
    }

    [Theory]
    [InlineData(AgentId, UserRole.Agent)]
    [InlineData(AdminId, UserRole.Admin)]
    public async Task AssignedAgentAndAdminCanAddAndViewComments(int userId, UserRole role)
    {
        await using var dbContext = await CreateDbContextAsync();
        await SeedTicketsAsync(dbContext);
        var service = new TicketService(dbContext, new SlaService(TimeProvider.System));
        var caller = CreatePrincipal(userId, role);

        var created = await service.CreateCommentAsync(
            101,
            new CreateTicketCommentRequest { CommentText = "Working on it." },
            caller,
            CancellationToken.None);
        var comments = await service.GetCommentsAsync(101, caller, CancellationToken.None);

        Assert.Equal(TicketOperationStatus.Success, created.Status);
        Assert.Equal(userId, created.Comment?.AuthorUserId);
        Assert.Single(comments!);
    }

    [Theory]
    [InlineData(OtherEmployeeId, UserRole.Employee, 101)]
    [InlineData(AgentId, UserRole.Agent, 103)]
    public async Task NonParticipantsCannotAddOrViewComments(
        int userId,
        UserRole role,
        int ticketId)
    {
        await using var dbContext = await CreateDbContextAsync();
        await SeedTicketsAsync(dbContext);
        var service = new TicketService(dbContext, new SlaService(TimeProvider.System));
        var caller = CreatePrincipal(userId, role);

        var created = await service.CreateCommentAsync(
            ticketId,
            new CreateTicketCommentRequest { CommentText = "Not authorized." },
            caller,
            CancellationToken.None);
        var comments = await service.GetCommentsAsync(ticketId, caller, CancellationToken.None);

        Assert.Equal(TicketOperationStatus.NotFound, created.Status);
        Assert.Null(comments);
        Assert.Empty(await dbContext.TicketComments.ToListAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("  \t\r\n ")]
    public async Task EmptyOrWhitespaceCommentIsRejected(string commentText)
    {
        await using var dbContext = await CreateDbContextAsync();
        await SeedTicketsAsync(dbContext);
        var service = new TicketService(dbContext, new SlaService(TimeProvider.System));

        var result = await service.CreateCommentAsync(
            101,
            new CreateTicketCommentRequest { CommentText = commentText },
            CreatePrincipal(EmployeeId, UserRole.Employee),
            CancellationToken.None);

        Assert.Equal(TicketOperationStatus.InvalidRequest, result.Status);
        Assert.Empty(await dbContext.TicketComments.ToListAsync());
    }

    [Fact]
    public async Task CommentLongerThanColumnLimitIsRejected()
    {
        await using var dbContext = await CreateDbContextAsync();
        await SeedTicketsAsync(dbContext);
        var service = new TicketService(dbContext, new SlaService(TimeProvider.System));

        var result = await service.CreateCommentAsync(
            101,
            new CreateTicketCommentRequest { CommentText = new string('x', 4001) },
            CreatePrincipal(EmployeeId, UserRole.Employee),
            CancellationToken.None);

        Assert.Equal(TicketOperationStatus.InvalidRequest, result.Status);
        Assert.Empty(await dbContext.TicketComments.ToListAsync());
    }

    [Theory]
    [InlineData(EmployeeId, UserRole.Employee)]
    [InlineData(AgentId, UserRole.Agent)]
    [InlineData(AdminId, UserRole.Admin)]
    public async Task CreatorAssignedAgentAndAdminCanViewHistory(int userId, UserRole role)
    {
        await using var dbContext = await CreateDbContextAsync();
        await SeedTicketsAsync(dbContext);
        await SeedHistoryAsync(dbContext);
        var service = new TicketService(dbContext, new SlaService(TimeProvider.System));

        var history = await service.GetHistoryAsync(
            101,
            CreatePrincipal(userId, role),
            CancellationToken.None);

        var entry = Assert.Single(history!);
        Assert.Equal(AdminId, entry.ChangedByUserId);
        Assert.Equal($"User {AdminId}", entry.ChangedByFullName);
    }

    [Theory]
    [InlineData(OtherEmployeeId, UserRole.Employee, 101)]
    [InlineData(AgentId, UserRole.Agent, 103)]
    public async Task NonParticipantsCannotViewHistory(
        int userId,
        UserRole role,
        int ticketId)
    {
        await using var dbContext = await CreateDbContextAsync();
        await SeedTicketsAsync(dbContext);
        await SeedHistoryAsync(dbContext);
        var service = new TicketService(dbContext, new SlaService(TimeProvider.System));

        var history = await service.GetHistoryAsync(
            ticketId,
            CreatePrincipal(userId, role),
            CancellationToken.None);

        Assert.Null(history);
    }

    [Fact]
    public void StatusEndpointRequiresAgentOrAdminPolicy()
    {
        var authorization = typeof(TicketsController)
            .GetMethod(nameof(TicketsController.UpdateStatus))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Single();

        Assert.Equal(AuthorizationPolicies.AgentOrAdmin, authorization.Policy);
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

    private static async Task SeedHistoryAsync(ApplicationDbContext dbContext)
    {
        var ticket = await dbContext.Tickets.SingleAsync(item => item.Id == 101);
        var actor = await dbContext.Users.SingleAsync(user => user.Id == AdminId);
        dbContext.TicketHistories.Add(new TicketHistory
        {
            Ticket = ticket,
            ChangedByUser = actor,
            Action = "Seeded test history.",
            ChangedAtUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();
    }

    private static async Task SetTicketStatusAsync(
        ApplicationDbContext dbContext,
        int ticketId,
        TicketStatus status)
    {
        var ticket = await dbContext.Tickets.SingleAsync(item => item.Id == ticketId);
        ticket.Status = status;
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
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(JwtOptions.RoleClaimType, role.ToString())
            ],
            JwtBearerDefaults.AuthenticationScheme,
            JwtRegisteredClaimNames.Sub,
            JwtOptions.RoleClaimType);

        return new ClaimsPrincipal(identity);
    }
}
