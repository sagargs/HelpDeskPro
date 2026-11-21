using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using HelpDeskPro.API.Configuration;
using HelpDeskPro.API.Data;
using HelpDeskPro.API.DTOs;
using HelpDeskPro.API.Enums;
using HelpDeskPro.API.Models;
using HelpDeskPro.API.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskPro.Tests;

public class DashboardServiceTests
{
    private static readonly DateTimeOffset NowUtc =
        new(2026, 11, 21, 8, 0, 0, TimeSpan.Zero);

    private const int EmployeeId = 1;
    private const int OtherEmployeeId = 2;
    private const int AgentId = 3;
    private const int OtherAgentId = 4;
    private const int InactiveAgentId = 5;
    private const int AdminId = 6;

    [Fact]
    public async Task AdminDashboardReturnsGlobalCountsAndActiveAgentWorkloads()
    {
        await using var dbContext = await CreateDbContextAsync(includeTickets: true);
        var service = CreateService(dbContext);

        var result = await service.GetSummaryAsync(
            CreatePrincipal(AdminId, UserRole.Admin),
            CancellationToken.None);

        Assert.Equal(TicketOperationStatus.Success, result.Status);
        var summary = result.Summary!;
        Assert.Equal(6, summary.TotalTicketCount);
        Assert.Equal(3, GetStatusCount(summary, TicketStatus.Open));
        Assert.Equal(1, GetStatusCount(summary, TicketStatus.InProgress));
        Assert.Equal(1, GetStatusCount(summary, TicketStatus.Resolved));
        Assert.Equal(1, GetStatusCount(summary, TicketStatus.Closed));
        Assert.Equal(2, GetPriorityCount(summary, TicketPriority.Low));
        Assert.Equal(1, GetPriorityCount(summary, TicketPriority.Medium));
        Assert.Equal(2, GetPriorityCount(summary, TicketPriority.High));
        Assert.Equal(1, GetPriorityCount(summary, TicketPriority.Critical));
        Assert.Equal(3, summary.OverdueTicketCount);

        Assert.Equal(2, summary.AgentWorkloads.Count);
        var agentWorkload = Assert.Single(
            summary.AgentWorkloads,
            item => item.AgentUserId == AgentId);
        Assert.Equal($"User {AgentId}", agentWorkload.AgentName);
        Assert.Equal(2, agentWorkload.ActiveAssignedTicketCount);
        Assert.Equal(1, agentWorkload.OverdueAssignedTicketCount);
        var otherAgentWorkload = Assert.Single(
            summary.AgentWorkloads,
            item => item.AgentUserId == OtherAgentId);
        Assert.Equal(1, otherAgentWorkload.ActiveAssignedTicketCount);
        Assert.Equal(1, otherAgentWorkload.OverdueAssignedTicketCount);
        Assert.DoesNotContain(
            summary.AgentWorkloads,
            item => item.AgentUserId == InactiveAgentId);
        Assert.DoesNotContain(nameof(User.PasswordHash), JsonSerializer.Serialize(summary));
    }

    [Fact]
    public async Task AgentDashboardIncludesOnlyAssignedTicketsAndOwnWorkload()
    {
        await using var dbContext = await CreateDbContextAsync(includeTickets: true);
        var service = CreateService(dbContext);

        var result = await service.GetSummaryAsync(
            CreatePrincipal(AgentId, UserRole.Agent),
            CancellationToken.None);

        Assert.Equal(TicketOperationStatus.Success, result.Status);
        var summary = result.Summary!;
        Assert.Equal(2, summary.TotalTicketCount);
        Assert.Equal(1, GetStatusCount(summary, TicketStatus.Open));
        Assert.Equal(1, GetStatusCount(summary, TicketStatus.InProgress));
        Assert.Equal(1, GetPriorityCount(summary, TicketPriority.High));
        Assert.Equal(1, GetPriorityCount(summary, TicketPriority.Medium));
        Assert.Equal(1, summary.OverdueTicketCount);
        var workload = Assert.Single(summary.AgentWorkloads);
        Assert.Equal(AgentId, workload.AgentUserId);
        Assert.Equal(2, workload.ActiveAssignedTicketCount);
        Assert.Equal(1, workload.OverdueAssignedTicketCount);
    }

    [Fact]
    public async Task EmployeeDashboardIncludesOnlyCreatedTicketsAndNoAgentWorkloads()
    {
        await using var dbContext = await CreateDbContextAsync(includeTickets: true);
        var service = CreateService(dbContext);

        var result = await service.GetSummaryAsync(
            CreatePrincipal(EmployeeId, UserRole.Employee),
            CancellationToken.None);

        Assert.Equal(TicketOperationStatus.Success, result.Status);
        var summary = result.Summary!;
        Assert.Equal(3, summary.TotalTicketCount);
        Assert.Equal(2, GetStatusCount(summary, TicketStatus.Open));
        Assert.Equal(1, GetStatusCount(summary, TicketStatus.Closed));
        Assert.Equal(2, summary.OverdueTicketCount);
        Assert.Empty(summary.AgentWorkloads);
    }

    [Fact]
    public async Task DashboardReturnsZeroMetricsForEmptyDatabase()
    {
        await using var dbContext = await CreateDbContextAsync(includeTickets: false, includeUsers: false);
        var service = CreateService(dbContext);

        var result = await service.GetSummaryAsync(
            CreatePrincipal(AdminId, UserRole.Admin),
            CancellationToken.None);

        Assert.Equal(TicketOperationStatus.Success, result.Status);
        Assert.Equal(0, result.Summary!.TotalTicketCount);
        Assert.Equal(0, result.Summary.OverdueTicketCount);
        Assert.All(result.Summary.CountsByStatus, item => Assert.Equal(0, item.Count));
        Assert.All(result.Summary.CountsByPriority, item => Assert.Equal(0, item.Count));
        Assert.Empty(result.Summary.AgentWorkloads);
    }

    private static DashboardService CreateService(ApplicationDbContext dbContext) =>
        new(dbContext, new SlaService(new FixedTimeProvider(NowUtc)));

    private static int GetStatusCount(
        DashboardSummaryResponse summary,
        TicketStatus status) =>
        summary.CountsByStatus.Single(item => item.Status == status).Count;

    private static int GetPriorityCount(
        DashboardSummaryResponse summary,
        TicketPriority priority) =>
        summary.CountsByPriority.Single(item => item.Priority == priority).Count;

    private static async Task<ApplicationDbContext> CreateDbContextAsync(
        bool includeTickets,
        bool includeUsers = true)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var dbContext = new ApplicationDbContext(options);

        if (includeUsers)
        {
            dbContext.Users.AddRange(
                CreateUser(EmployeeId, UserRole.Employee),
                CreateUser(OtherEmployeeId, UserRole.Employee),
                CreateUser(AgentId, UserRole.Agent),
                CreateUser(OtherAgentId, UserRole.Agent),
                CreateUser(InactiveAgentId, UserRole.Agent, isActive: false),
                CreateUser(AdminId, UserRole.Admin));
        }

        if (includeTickets)
        {
            dbContext.Tickets.AddRange(
                CreateTicket(101, EmployeeId, AgentId, TicketStatus.Open, TicketPriority.High, -1),
                CreateTicket(102, OtherEmployeeId, AgentId, TicketStatus.InProgress, TicketPriority.Medium, 4),
                CreateTicket(103, OtherEmployeeId, OtherAgentId, TicketStatus.Resolved, TicketPriority.High, -2),
                CreateTicket(104, EmployeeId, null, TicketStatus.Closed, TicketPriority.Low, -3),
                CreateTicket(105, OtherEmployeeId, OtherAgentId, TicketStatus.Open, TicketPriority.Critical, -4),
                CreateTicket(106, EmployeeId, InactiveAgentId, TicketStatus.Open, TicketPriority.Low, -5));
        }

        await dbContext.SaveChangesAsync();
        return dbContext;
    }

    private static User CreateUser(int id, UserRole role, bool isActive = true) =>
        new()
        {
            Id = id,
            FullName = $"User {id}",
            Email = $"user{id}@example.com",
            PasswordHash = "test-hash",
            Role = role,
            IsActive = isActive,
            CreatedAtUtc = NowUtc.UtcDateTime
        };

    private static Ticket CreateTicket(
        int id,
        int creatorUserId,
        int? assignedToUserId,
        TicketStatus status,
        TicketPriority priority,
        int dueOffsetHours) =>
        new()
        {
            Id = id,
            Title = $"Ticket {id}",
            Description = "Dashboard test ticket",
            CategoryId = 10,
            CreatedByUserId = creatorUserId,
            AssignedToUserId = assignedToUserId,
            Status = status,
            Priority = priority,
            CreatedAtUtc = NowUtc.UtcDateTime,
            UpdatedAtUtc = NowUtc.UtcDateTime,
            DueAtUtc = NowUtc.UtcDateTime.AddHours(dueOffsetHours)
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
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
