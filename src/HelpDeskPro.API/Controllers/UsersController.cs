using HelpDeskPro.API.Configuration;
using HelpDeskPro.API.DTOs;
using HelpDeskPro.API.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HelpDeskPro.API.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public sealed class UsersController(IAgentDirectoryService agentDirectoryService) : ControllerBase
{
    [HttpGet("agents")]
    [ProducesResponseType<IReadOnlyList<AgentListItemResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<AgentListItemResponse>>> GetAgents(
        CancellationToken cancellationToken)
    {
        var agents = await agentDirectoryService.GetActiveAgentsAsync(cancellationToken);
        return Ok(agents);
    }
}
