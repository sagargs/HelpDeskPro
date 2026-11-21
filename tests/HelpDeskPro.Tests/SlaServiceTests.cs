using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using HelpDeskPro.API.Configuration;
using HelpDeskPro.API.Data;
using HelpDeskPro.API.DTOs;
using HelpDeskPro.API.Enums;
using HelpDeskPro.API.Models;
using HelpDeskPro.API.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskPro.Tests;

public class SlaServiceTests
{
    private static readonly DateTimeOffset StartUtc =
        new(2026, 11, 21, 8, 0, 0, TimeSpan.Zero);

    private const int EmployeeId = 1;
    private const int OtherEmployeeId = 2;
    private const int AgentId = 3;
    private const int OtherAgentId = 4;
    private const int AdminId = 5;
    private const int CategoryId = 10;

    [Theory]
    [InlineData(TicketPriority.Low, 72)]
    [InlineData(TicketPriority.Medium, 48)]
    [InlineData(TicketPriority.High, 24)]
    [InlineData(TicketPriority.Critical, 4)]
    public void GetSlaDurationUsesConfiguredCalendarHours(TicketPriority priority, int expectedHours)
    {
        var service = new SlaService(new FixedTimeProvider(StartUtc));

        Assert.Equal(TimeSpan.FromHours(expectedHours), service.GetSlaDuration(priority));
    }

    [Fact]
    public void CalculateDueAtUtcNormalizesOffsetInput()
    {
        var service = new SlaService(new FixedTimeProvider(StartUtc));
        var startAtUtc = new DateTimeOffset(2026, 11, 21, 13, 30, 0, TimeSpan.FromHours(5.5));

        var dueAtUtc = service.CalculateDueAtUtc(TicketPriority.High, startAtUtc);

        Assert.Equal(startAtUtc.ToUniversalTime().AddHours(24).UtcDateTime, dueAtUtc);
        Assert.Equal(DateTimeKind.Utc, dueAtUtc.Kind);
    }

    [Theory]
    [InlineData(TicketPriority.Low, 72)]
    [InlineData(TicketPriority.Medium, 48)]
    [InlineData(TicketPriority.High, 24)]
    [InlineData(TicketPriority.Critical, 4)]
    public async Task CreatingTicketSetsDeadlineFromUtcCreationTime(
        TicketPriority priority,
        int expectedHours)
    {
        await using var dbContext = await CreateDbContextAsync();
        var timeProvider = new FixedTimeProvider(StartUtc);
        var slaService = new SlaService(timeProvider);
        var ticketService = new TicketService(dbContext, slaService);

        var result = await ticketService.CreateAsync(
            CreateTicketRequest(priority),
            CreatePrincipal(EmployeeId, UserRole.Employee),
            CancellationToken.None);
        var ticket = await dbContext.Tickets.SingleAsync();

        Assert.Equal(TicketOperationStatus.Success, result.Status);
        Assert.Equal(StartUtc.UtcDateTime, ticket.CreatedAtUtc);
        Assert.Equal(StartUtc.UtcDateTime.AddHours(expectedHours), ticket.DueAtUtc);
        Assert.Equal(DateTimeKind.Utc, ticket.DueAtUtc?.Kind);
        Assert.Equal(
            new DateTimeOffset(StartUtc.UtcDateTime.AddHours(expectedHours)),
            result.Ticket?.DueAtUtc);
    }

    [Fact]
    public async Task ChangingPriorityRecalculatesDeadlineFromChangeTime()
    {
        await using var dbContext = await CreateDbContextAsync();
        var timeProvider = new FixedTimeProvider(StartUtc);
        var ticketService = new TicketService(dbContext, new SlaService(timeProvider));
        await ticketService.CreateAsync(
            CreateTicketRequest(TicketPriority.Medium),
            CreatePrincipal(EmployeeId, UserRole.Employee),
            CancellationToken.None);
        var ticket = await dbContext.Tickets.SingleAsync();
        var originalDeadline = ticket.DueAtUtc;

        timeProvider.Advance(TimeSpan.FromHours(7));
        var result = await ticketService.UpdateAsync(
            ticket.Id,
            UpdateRequest("Updated title", "Updated description", TicketPriority.Critical),
            CreatePrincipal(EmployeeId, UserRole.Employee),
            CancellationToken.None);

        Assert.Equal(TicketOperationStatus.Success, result.Status);
        Assert.Equal(timeProvider.GetUtcNow().UtcDateTime.AddHours(4), ticket.DueAtUtc);
        Assert.NotEqual(originalDeadline, ticket.DueAtUtc);
    }

    [Fact]
    public async Task UnrelatedTicketChangesDoNotRecalculateDeadline()
    {
        await using var dbContext = await CreateDbContextAsync();
        var timeProvider = new FixedTimeProvider(StartUtc);
        var ticketService = new TicketService(dbContext, new SlaService(timeProvider));
        await ticketService.CreateAsync(
            CreateTicketRequest(TicketPriority.High),
            CreatePrincipal(EmployeeId, UserRole.Employee),
            CancellationToken.None);
        var ticket = await dbContext.Tickets.SingleAsync();
        var originalDeadline = ticket.DueAtUtc;
        timeProvider.Advance(TimeSpan.FromHours(2));

        var result = await ticketService.UpdateAsync(
            ticket.Id,
            UpdateRequest("Updated title", "Updated description", TicketPriority.High),
            CreatePrincipal(EmployeeId, UserRole.Employee),
            CancellationToken.None);

        Assert.Equal(TicketOperationStatus.Success, result.Status);
        Assert.Equal(originalDeadline, ticket.DueAtUtc);
    }

    [Fact]
    public void OverdueComparisonIsStrictAtTheDeadline()
    {
        var timeProvider = new FixedTimeProvider(StartUtc);
        var service = new SlaService(timeProvider);
        var ticket = CreateTicket(
            101,
            EmployeeId,
            AgentId,
            TicketStatus.Open,
            StartUtc.UtcDateTime);

        Assert.False(service.IsOverdue(ticket));

        timeProvider.Advance(TimeSpan.FromTicks(1));

        Assert.True(service.IsOverdue(ticket));
    }

    [Theory]
    [InlineData(TicketStatus.Resolved)]
    [InlineData(TicketStatus.Closed)]
    public void ResolvedAndClosedTicketsAreNeverOverdue(TicketStatus status)
    {
        var service = new SlaService(new FixedTimeProvider(StartUtc));
        var ticket = CreateTicket(
            101,
            EmployeeId,
            AgentId,
            status,
            StartUtc.UtcDateTime.AddTicks(-1));

        Assert.False(service.IsOverdue(ticket));
    }

    [Fact]
    public void TicketWithoutDeadlineIsNotOverdue()
    {
        var service = new SlaService(new FixedTimeProvider(StartUtc));
        var ticket = CreateTicket(101, EmployeeId, null, TicketStatus.Open, null);

        Assert.False(service.IsOverdue(ticket));
    }

    [Fact]
    public async Task ReopeningResolvedTicketPreservesDeadlineAndRestoresOverdueState()
    {
        await using var dbContext = await CreateDbContextAsync();
        var dueAtUtc = StartUtc.UtcDateTime.AddHours(-1);
        await SeedTicketAsync(dbContext, 101, EmployeeId, AgentId, TicketStatus.Resolved, dueAtUtc);
        var ticketService = new TicketService(
            dbContext,
            new SlaService(new FixedTimeProvider(StartUtc)));

        Assert.False((await ticketService.GetByIdAsync(
            101,
            CreatePrincipal(AgentId, UserRole.Agent),
            CancellationToken.None))!.IsOverdue);

        var result = await ticketService.UpdateStatusAsync(
            101,
            new UpdateTicketStatusRequest { Status = TicketStatus.InProgress },
            CreatePrincipal(AgentId, UserRole.Agent),
            CancellationToken.None);

        Assert.Equal(TicketOperationStatus.Success, result.Status);
        Assert.Equal(dueAtUtc, (await dbContext.Tickets.SingleAsync()).DueAtUtc);
        Assert.True(result.Ticket!.IsOverdue);
    }

    [Fact]
    public async Task OverdueTicketQueryPreservesAdminAgentAndEmployeeVisibility()
    {
        await using var dbContext = await CreateDbContextAsync();
        await SeedOverdueTicketsAsync(dbContext);
        var ticketService = new TicketService(
            dbContext,
            new SlaService(new FixedTimeProvider(StartUtc)));

        var employeeTickets = await ticketService.GetOverdueAsync(
            CreatePrincipal(EmployeeId, UserRole.Employee),
            CancellationToken.None);
        var agentTickets = await ticketService.GetOverdueAsync(
            CreatePrincipal(AgentId, UserRole.Agent),
            CancellationToken.None);
        var adminTickets = await ticketService.GetOverdueAsync(
            CreatePrincipal(AdminId, UserRole.Admin),
            CancellationToken.None);

        Assert.Equal([101, 103], employeeTickets.Select(ticket => ticket.Id).Order().ToArray());
        Assert.Equal([101], agentTickets.Select(ticket => ticket.Id).Order().ToArray());
        Assert.Equal([101, 102, 103], adminTickets.Select(ticket => ticket.Id).Order().ToArray());
        Assert.All(adminTickets, ticket => Assert.True(ticket.IsOverdue));
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

    private static async Task SeedTicketAsync(
        ApplicationDbContext dbContext,
        int ticketId,
        int createdByUserId,
        int? assignedToUserId,
        TicketStatus status,
        DateTime? dueAtUtc)
    {
        dbContext.Tickets.Add(CreateTicket(
            ticketId,
            createdByUserId,
            assignedToUserId,
            status,
            dueAtUtc));
        await dbContext.SaveChangesAsync();
    }

    private static async Task SeedOverdueTicketsAsync(ApplicationDbContext dbContext)
    {
        var overdueAtUtc = StartUtc.UtcDateTime.AddTicks(-1);
        dbContext.Tickets.AddRange(
            CreateTicket(101, EmployeeId, AgentId, TicketStatus.Open, overdueAtUtc),
            CreateTicket(102, OtherEmployeeId, OtherAgentId, TicketStatus.Open, overdueAtUtc),
            CreateTicket(103, EmployeeId, null, TicketStatus.InProgress, overdueAtUtc),
            CreateTicket(104, EmployeeId, AgentId, TicketStatus.Resolved, overdueAtUtc),
            CreateTicket(105, EmployeeId, AgentId, TicketStatus.Closed, overdueAtUtc),
            CreateTicket(106, EmployeeId, AgentId, TicketStatus.Open, StartUtc.UtcDateTime),
            CreateTicket(107, EmployeeId, AgentId, TicketStatus.Open, StartUtc.UtcDateTime.AddHours(1)));
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
            CreatedAtUtc = StartUtc.UtcDateTime
        };

    private static Ticket CreateTicket(
        int id,
        int createdByUserId,
        int? assignedToUserId,
        TicketStatus status,
        DateTime? dueAtUtc) =>
        new()
        {
            Id = id,
            Title = $"Ticket {id}",
            Description = "Ticket description",
            CategoryId = CategoryId,
            CreatedByUserId = createdByUserId,
            AssignedToUserId = assignedToUserId,
            Priority = TicketPriority.Medium,
            Status = status,
            CreatedAtUtc = StartUtc.UtcDateTime,
            UpdatedAtUtc = StartUtc.UtcDateTime,
            DueAtUtc = dueAtUtc
        };

    private static CreateTicketRequest CreateTicketRequest(TicketPriority priority) =>
        new()
        {
            Title = "SLA ticket",
            Description = "Ticket used to verify SLA deadlines.",
            CategoryId = CategoryId,
            Priority = priority
        };

    private static UpdateTicketRequest UpdateRequest(
        string title,
        string description,
        TicketPriority priority) =>
        new()
        {
            Title = title,
            Description = description,
            CategoryId = CategoryId,
            Priority = priority
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

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset CurrentUtcNow { get; private set; } = utcNow.ToUniversalTime();

        public override DateTimeOffset GetUtcNow() => CurrentUtcNow;

        public void Advance(TimeSpan amount) => CurrentUtcNow = CurrentUtcNow.Add(amount);
    }
}
