using HelpDeskPro.API.Data;
using HelpDeskPro.API.DTOs;
using HelpDeskPro.API.Enums;
using HelpDeskPro.API.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskPro.API.Services;

public sealed class AgentDirectoryService(ApplicationDbContext dbContext) : IAgentDirectoryService
{
    public async Task<IReadOnlyList<AgentListItemResponse>> GetActiveAgentsAsync(
        CancellationToken cancellationToken) =>
        await dbContext.Users
            .AsNoTracking()
            .Where(user => user.Role == UserRole.Agent && user.IsActive)
            .OrderBy(user => user.FullName)
            .ThenBy(user => user.Id)
            .Select(user => new AgentListItemResponse(user.Id, user.FullName, user.Email))
            .ToListAsync(cancellationToken);
}
