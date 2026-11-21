using HelpDeskPro.API.DTOs;

namespace HelpDeskPro.API.Interfaces;

public interface IAgentDirectoryService
{
    Task<IReadOnlyList<AgentListItemResponse>> GetActiveAgentsAsync(
        CancellationToken cancellationToken);
}
