using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using HelpDeskPro.API.Configuration;
using HelpDeskPro.API.Data;
using HelpDeskPro.API.DTOs;
using HelpDeskPro.API.Enums;
using HelpDeskPro.API.Interfaces;
using HelpDeskPro.API.Models;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskPro.API.Services;

public sealed class DashboardService(
    ApplicationDbContext dbContext,
    ISlaService slaService) : IDashboardService
{
    public async Task<DashboardSummaryResult> GetSummaryAsync(
        ClaimsPrincipal caller,
        CancellationToken cancellationToken)
    {
        if (!TryGetCaller(caller, out var userId, out var role))
        {
            return new DashboardSummaryResult(TicketOperationStatus.Forbidden);
        }

        var tickets = GetVisibleTickets(userId, role).AsNoTracking();
        var asOfUtc = slaService.GetUtcNow();

        var totalTicketCount = await tickets.CountAsync(cancellationToken);
        var statusCounts = await tickets
            .GroupBy(ticket => ticket.Status)
            .Select(group => new TicketStatusCountResponse(group.Key, group.Count()))
            .ToListAsync(cancellationToken);
        var priorityCounts = await tickets
            .GroupBy(ticket => ticket.Priority)
            .Select(group => new TicketPriorityCountResponse(group.Key, group.Count()))
            .ToListAsync(cancellationToken);
        var overdueTicketCount = await slaService
            .GetOverdueTickets(tickets, asOfUtc)
            .CountAsync(cancellationToken);
        var agentWorkloads = await GetAgentWorkloadsAsync(
            userId,
            role,
            asOfUtc,
            cancellationToken);

        var countsByStatus = statusCounts.ToDictionary(item => item.Status, item => item.Count);
        var countsByPriority = priorityCounts.ToDictionary(item => item.Priority, item => item.Count);
        var summary = new DashboardSummaryResponse(
            totalTicketCount,
            Enum.GetValues<TicketStatus>()
                .Select(status => new TicketStatusCountResponse(
                    status,
                    countsByStatus.GetValueOrDefault(status)))
                .ToArray(),
            Enum.GetValues<TicketPriority>()
                .Select(priority => new TicketPriorityCountResponse(
                    priority,
                    countsByPriority.GetValueOrDefault(priority)))
                .ToArray(),
            overdueTicketCount,
            agentWorkloads);

        return new DashboardSummaryResult(TicketOperationStatus.Success, summary);
    }

    private IQueryable<Ticket> GetVisibleTickets(int userId, UserRole role) =>
        role switch
        {
            UserRole.Admin => dbContext.Tickets,
            UserRole.Agent => dbContext.Tickets.Where(ticket => ticket.AssignedToUserId == userId),
            UserRole.Employee => dbContext.Tickets.Where(ticket => ticket.CreatedByUserId == userId),
            _ => dbContext.Tickets.Where(_ => false)
        };

    private async Task<IReadOnlyList<AgentWorkloadResponse>> GetAgentWorkloadsAsync(
        int callerUserId,
        UserRole role,
        DateTimeOffset asOfUtc,
        CancellationToken cancellationToken)
    {
        if (role == UserRole.Employee)
        {
            return Array.Empty<AgentWorkloadResponse>();
        }

        IQueryable<User> agentsQuery = dbContext.Users
            .AsNoTracking()
            .Where(user => user.Role == UserRole.Agent);
        agentsQuery = role == UserRole.Admin
            ? agentsQuery.Where(user => user.IsActive)
            : agentsQuery.Where(user => user.Id == callerUserId);

        var agents = await agentsQuery
            .OrderBy(user => user.Id)
            .Select(user => new { user.Id, user.FullName })
            .ToListAsync(cancellationToken);
        if (agents.Count == 0)
        {
            return Array.Empty<AgentWorkloadResponse>();
        }

        var agentIds = agents.Select(agent => agent.Id).ToArray();
        var assignedTickets = dbContext.Tickets
            .AsNoTracking()
            .Where(ticket =>
                ticket.AssignedToUserId.HasValue
                && agentIds.Contains(ticket.AssignedToUserId.Value));

        var activeCounts = await assignedTickets
            .Where(ticket =>
                ticket.Status != TicketStatus.Resolved
                && ticket.Status != TicketStatus.Closed)
            .GroupBy(ticket => ticket.AssignedToUserId!.Value)
            .Select(group => new { AgentUserId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.AgentUserId, item => item.Count, cancellationToken);

        var overdueCounts = await slaService
            .GetOverdueTickets(assignedTickets, asOfUtc)
            .GroupBy(ticket => ticket.AssignedToUserId!.Value)
            .Select(group => new { AgentUserId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.AgentUserId, item => item.Count, cancellationToken);

        return agents
            .Select(agent => new AgentWorkloadResponse(
                agent.Id,
                agent.FullName,
                activeCounts.GetValueOrDefault(agent.Id),
                overdueCounts.GetValueOrDefault(agent.Id)))
            .ToArray();
    }

    private static bool TryGetCaller(
        ClaimsPrincipal caller,
        out int userId,
        out UserRole role)
    {
        userId = 0;
        role = default;

        if (caller.Identity?.IsAuthenticated != true
            || !int.TryParse(
                caller.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out userId)
            || userId <= 0)
        {
            return false;
        }

        if (caller.IsInRole(nameof(UserRole.Admin)))
        {
            role = UserRole.Admin;
            return true;
        }

        if (caller.IsInRole(nameof(UserRole.Agent)))
        {
            role = UserRole.Agent;
            return true;
        }

        if (caller.IsInRole(nameof(UserRole.Employee)))
        {
            role = UserRole.Employee;
            return true;
        }

        return false;
    }
}
