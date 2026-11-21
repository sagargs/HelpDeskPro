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

public class TicketQueryTests
{
    private static readonly DateTime NowUtc =
        new(2026, 11, 21, 8, 0, 0, DateTimeKind.Utc);

    private const int EmployeeId = 1;
    private const int OtherEmployeeId = 2;
    private const int AgentId = 3;
    private const int OtherAgentId = 4;
    private const int AdminId = 5;

    [Fact]
    public async Task SearchFindsTitleMatches()
    {
        var result = await QueryAsync(new TicketQueryParameters { Search = "access failure" });

        Assert.Equal([101], GetIds(result));
    }

    [Fact]
    public async Task SearchFindsDescriptionMatches()
    {
        var result = await QueryAsync(new TicketQueryParameters { Search = "VPN client" });

        Assert.Equal([102], GetIds(result));
    }

    [Fact]
    public async Task StatusFilterReturnsOnlyMatchingTickets()
    {
        var result = await QueryAsync(new TicketQueryParameters { Status = TicketStatus.Open });

        Assert.Equal([101, 105], GetIds(result).Order().ToArray());
    }

    [Fact]
    public async Task PriorityFilterReturnsOnlyMatchingTickets()
    {
        var result = await QueryAsync(new TicketQueryParameters { Priority = TicketPriority.Critical });

        Assert.Equal([102, 105], GetIds(result).Order().ToArray());
    }

    [Fact]
    public async Task CategoryFilterReturnsOnlyMatchingTickets()
    {
        var result = await QueryAsync(new TicketQueryParameters { CategoryId = 10 });

        Assert.Equal([101, 103, 105], GetIds(result).Order().ToArray());
    }

    [Fact]
    public async Task AssignedAgentFilterReturnsOnlyMatchingTickets()
    {
        var result = await QueryAsync(new TicketQueryParameters { AssignedToUserId = AgentId });

        Assert.Equal([101, 104, 105], GetIds(result).Order().ToArray());
    }

    [Fact]
    public async Task CreatorFilterReturnsOnlyMatchingTickets()
    {
        var result = await QueryAsync(new TicketQueryParameters { CreatedByUserId = EmployeeId });

        Assert.Equal([101, 102, 105], GetIds(result).Order().ToArray());
    }

    [Fact]
    public async Task OverdueFilterIncludesOverdueOpenTickets()
    {
        var result = await QueryAsync(new TicketQueryParameters { IsOverdue = true });

        Assert.Equal([102], GetIds(result));
        Assert.True(result.Page!.Items[0].IsOverdue);
    }

    [Fact]
    public async Task NotOverdueFilterIncludesResolvedClosedAndNotDueTickets()
    {
        var result = await QueryAsync(new TicketQueryParameters { IsOverdue = false });

        Assert.Equal([101, 103, 104, 105], GetIds(result).Order().ToArray());
        Assert.All(result.Page!.Items, ticket => Assert.False(ticket.IsOverdue));
    }

    [Fact]
    public async Task CombinedFiltersAreAppliedTogether()
    {
        var result = await QueryAsync(new TicketQueryParameters
        {
            Status = TicketStatus.Open,
            Priority = TicketPriority.Critical,
            CategoryId = 10,
            AssignedToUserId = AgentId,
            CreatedByUserId = EmployeeId,
            IsOverdue = false
        });

        Assert.Equal([105], GetIds(result));
    }

    [Theory]
    [InlineData("createdAtUtc", "asc", "102,101,104,103,105")]
    [InlineData("createdAtUtc", "desc", "105,103,104,101,102")]
    [InlineData("updatedAtUtc", "asc", "103,101,104,102,105")]
    [InlineData("updatedAtUtc", "desc", "105,102,104,101,103")]
    [InlineData("dueAtUtc", "asc", "104,103,102,105,101")]
    [InlineData("dueAtUtc", "desc", "101,105,102,103,104")]
    [InlineData("priority", "asc", "103,104,101,102,105")]
    [InlineData("priority", "desc", "102,105,101,104,103")]
    [InlineData("status", "asc", "101,105,102,103,104")]
    [InlineData("status", "desc", "104,103,102,101,105")]
    public async Task SortingSupportsEachFieldAndDirection(
        string sortBy,
        string sortDirection,
        string expectedIds)
    {
        var result = await QueryAsync(new TicketQueryParameters
        {
            SortBy = sortBy,
            SortDirection = sortDirection
        });

        Assert.Equal(
            expectedIds.Split(',').Select(int.Parse),
            GetIds(result));
    }

    [Fact]
    public async Task DefaultSortingIsCreatedAtDescendingWithStableIdTieBreak()
    {
        await using var dbContext = await CreateDbContextAsync();
        await SeedTicketsAsync(dbContext);
        dbContext.Tickets.Add(CreateTicket(
            100,
            EmployeeId,
            AgentId,
            10,
            TicketPriority.Low,
            TicketStatus.Open,
            5,
            2,
            10,
            "Same timestamp ticket",
            "Additional ticket"));
        await dbContext.SaveChangesAsync();
        var result = await QueryAsync(
            dbContext,
            new TicketQueryParameters(),
            AdminId,
            UserRole.Admin);

        Assert.Equal([100, 105, 103, 104, 101, 102], GetIds(result));
    }

    [Fact]
    public async Task DefaultPaginationUsesPageOneAndPageSizeTen()
    {
        var result = await QueryAsync(new TicketQueryParameters());

        Assert.Equal(1, result.Page!.PageNumber);
        Assert.Equal(10, result.Page.PageSize);
        Assert.Equal(5, result.Page.TotalCount);
        Assert.Equal(1, result.Page.TotalPages);
        Assert.Equal(5, result.Page.Items.Count);
    }

    [Fact]
    public async Task PaginationReturnsCorrectCountsAndPageFlags()
    {
        var page = await QueryAsync(new TicketQueryParameters { PageNumber = 2, PageSize = 2 });
        var firstPage = await QueryAsync(new TicketQueryParameters { PageSize = 2 });
        var lastPage = await QueryAsync(new TicketQueryParameters { PageNumber = 3, PageSize = 2 });

        Assert.Equal(5, page.Page!.TotalCount);
        Assert.Equal(3, page.Page.TotalPages);
        Assert.Equal(2, page.Page.Items.Count);
        Assert.True(page.Page.HasPreviousPage);
        Assert.True(page.Page.HasNextPage);
        Assert.False(firstPage.Page!.HasPreviousPage);
        Assert.True(firstPage.Page.HasNextPage);
        Assert.Single(lastPage.Page!.Items);
        Assert.True(lastPage.Page.HasPreviousPage);
        Assert.False(lastPage.Page.HasNextPage);

        var beyondLastPage = await QueryAsync(
            new TicketQueryParameters { PageNumber = 4, PageSize = 2 });
        Assert.Empty(beyondLastPage.Page!.Items);
        Assert.Equal(5, beyondLastPage.Page.TotalCount);
        Assert.Equal(3, beyondLastPage.Page.TotalPages);
    }

    [Fact]
    public async Task MaximumPageSizeOfOneHundredIsAccepted()
    {
        var result = await QueryAsync(new TicketQueryParameters { PageSize = 100 });

        Assert.Equal(TicketOperationStatus.Success, result.Status);
        Assert.Equal(100, result.Page!.PageSize);
        Assert.Equal(5, result.Page.Items.Count);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task InvalidPageValuesAreRejected(int pageNumber, int pageSize)
    {
        var result = await QueryAsync(new TicketQueryParameters
        {
            PageNumber = pageNumber,
            PageSize = pageSize
        });

        Assert.Equal(TicketOperationStatus.InvalidRequest, result.Status);
    }

    [Fact]
    public async Task UnsupportedSortFieldAndDirectionAreRejected()
    {
        var invalidField = await QueryAsync(new TicketQueryParameters { SortBy = "title" });
        var invalidDirection = await QueryAsync(new TicketQueryParameters { SortDirection = "sideways" });

        Assert.Equal(TicketOperationStatus.InvalidRequest, invalidField.Status);
        Assert.Equal(TicketOperationStatus.InvalidRequest, invalidDirection.Status);
    }

    [Fact]
    public async Task InvalidEnumAndFilterValuesAreRejected()
    {
        var invalidStatus = await QueryAsync(new TicketQueryParameters
        {
            Status = (TicketStatus)999
        });
        var invalidPriority = await QueryAsync(new TicketQueryParameters
        {
            Priority = (TicketPriority)999
        });
        var invalidCategory = await QueryAsync(new TicketQueryParameters { CategoryId = 0 });
        var invalidAssignedUser = await QueryAsync(new TicketQueryParameters { AssignedToUserId = -1 });
        var invalidCreator = await QueryAsync(new TicketQueryParameters { CreatedByUserId = 0 });

        Assert.Equal(TicketOperationStatus.InvalidRequest, invalidStatus.Status);
        Assert.Equal(TicketOperationStatus.InvalidRequest, invalidPriority.Status);
        Assert.Equal(TicketOperationStatus.InvalidRequest, invalidCategory.Status);
        Assert.Equal(TicketOperationStatus.InvalidRequest, invalidAssignedUser.Status);
        Assert.Equal(TicketOperationStatus.InvalidRequest, invalidCreator.Status);
    }

    [Fact]
    public async Task EmptyResultHasZeroCountAndPages()
    {
        var result = await QueryAsync(new TicketQueryParameters { Search = "no matching ticket" });

        Assert.Empty(result.Page!.Items);
        Assert.Equal(0, result.Page.TotalCount);
        Assert.Equal(0, result.Page.TotalPages);
        Assert.False(result.Page.HasPreviousPage);
        Assert.False(result.Page.HasNextPage);
    }

    [Fact]
    public async Task EmployeeVisibilityIsAppliedBeforeFiltersAndPagination()
    {
        var result = await QueryAsync(
            new TicketQueryParameters { PageSize = 1 },
            EmployeeId,
            UserRole.Employee);

        Assert.Equal(3, result.Page!.TotalCount);
        Assert.Equal(3, result.Page.TotalPages);
        Assert.Equal([105], GetIds(result));
        Assert.All(result.Page.Items, ticket => Assert.Equal(EmployeeId, ticket.CreatedByUserId));
    }

    [Fact]
    public async Task AgentVisibilityIsAppliedBeforeFiltersAndPagination()
    {
        var result = await QueryAsync(
            new TicketQueryParameters { PageSize = 2 },
            AgentId,
            UserRole.Agent);

        Assert.Equal(3, result.Page!.TotalCount);
        Assert.Equal([105, 104], GetIds(result));
        Assert.All(result.Page.Items, ticket => Assert.Equal(AgentId, ticket.AssignedToUserId));
    }

    [Fact]
    public async Task AdminCanSeeAllTicketsBeforePagination()
    {
        var result = await QueryAsync(
            new TicketQueryParameters { PageSize = 3 },
            AdminId,
            UserRole.Admin);

        Assert.Equal(5, result.Page!.TotalCount);
        Assert.Equal(2, result.Page.TotalPages);
        Assert.Equal(3, result.Page.Items.Count);
    }

    [Fact]
    public async Task QueryFiltersCannotExposeTicketsOutsideCallerVisibility()
    {
        var employeeResult = await QueryAsync(
            new TicketQueryParameters { CreatedByUserId = OtherEmployeeId },
            EmployeeId,
            UserRole.Employee);
        var agentResult = await QueryAsync(
            new TicketQueryParameters { AssignedToUserId = OtherAgentId },
            AgentId,
            UserRole.Agent);

        Assert.Equal(0, employeeResult.Page!.TotalCount);
        Assert.Equal(0, agentResult.Page!.TotalCount);
        Assert.Empty(employeeResult.Page.Items);
        Assert.Empty(agentResult.Page.Items);
    }

    private static int[] GetIds(TicketListOperationResult result) =>
        result.Page!.Items.Select(ticket => ticket.Id).ToArray();

    private static Task<TicketListOperationResult> QueryAsync(
        TicketQueryParameters parameters,
        int userId = AdminId,
        UserRole role = UserRole.Admin) =>
        QueryAsync(
            CreateDbContextAsync(),
            parameters,
            userId,
            role);

    private static async Task<TicketListOperationResult> QueryAsync(
        Task<ApplicationDbContext> contextTask,
        TicketQueryParameters parameters,
        int userId,
        UserRole role)
    {
        await using var dbContext = await contextTask;
        await SeedTicketsAsync(dbContext);
        return await QueryAsync(dbContext, parameters, userId, role);
    }

    private static async Task<TicketListOperationResult> QueryAsync(
        ApplicationDbContext dbContext,
        TicketQueryParameters parameters,
        int userId,
        UserRole role)
    {
        var slaService = new SlaService(new FixedTimeProvider(new DateTimeOffset(NowUtc)));
        var ticketService = new TicketService(dbContext, slaService);
        return await ticketService.GetAllAsync(
            parameters,
            CreatePrincipal(userId, role),
            CancellationToken.None);
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
        dbContext.Categories.AddRange(
            CreateCategory(10, "Hardware"),
            CreateCategory(11, "Software"));
        await dbContext.SaveChangesAsync();
        return dbContext;
    }

    private static async Task SeedTicketsAsync(ApplicationDbContext dbContext)
    {
        dbContext.Tickets.AddRange(
            CreateTicket(
                101, EmployeeId, AgentId, 10, TicketPriority.High, TicketStatus.Open,
                2, 2, 12, "VPN access failure", "Cannot reach secure network."),
            CreateTicket(
                102, EmployeeId, OtherAgentId, 11, TicketPriority.Critical, TicketStatus.InProgress,
                1, 4, -1, "Laptop issue", "VPN client installation fails."),
            CreateTicket(
                103, OtherEmployeeId, null, 10, TicketPriority.Low, TicketStatus.Resolved,
                4, 1, -5, "Mail outage", "Mailbox unavailable."),
            CreateTicket(
                104, OtherEmployeeId, AgentId, 11, TicketPriority.Medium, TicketStatus.Closed,
                3, 3, -10, "Printer issue", "Printer is offline."),
            CreateTicket(
                105, EmployeeId, AgentId, 10, TicketPriority.Critical, TicketStatus.Open,
                5, 5, 10, "Access request", "Requesting application access."));
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
            CreatedAtUtc = NowUtc
        };

    private static Category CreateCategory(int id, string name) =>
        new()
        {
            Id = id,
            Name = name,
            Description = $"{name} support",
            IsActive = true
        };

    private static Ticket CreateTicket(
        int id,
        int creatorId,
        int? assignedAgentId,
        int categoryId,
        TicketPriority priority,
        TicketStatus status,
        int createdOffsetHours,
        int updatedOffsetHours,
        int dueOffsetHours,
        string title,
        string description) =>
        new()
        {
            Id = id,
            Title = title,
            Description = description,
            CategoryId = categoryId,
            CreatedByUserId = creatorId,
            AssignedToUserId = assignedAgentId,
            Priority = priority,
            Status = status,
            CreatedAtUtc = NowUtc.AddHours(createdOffsetHours),
            UpdatedAtUtc = NowUtc.AddHours(updatedOffsetHours),
            DueAtUtc = NowUtc.AddHours(dueOffsetHours)
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
