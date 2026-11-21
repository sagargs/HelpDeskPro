using HelpDeskPro.API.Configuration;
using HelpDeskPro.API.DTOs;
using HelpDeskPro.API.Interfaces;
using HelpDeskPro.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HelpDeskPro.API.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize(Policy = AuthorizationPolicies.AuthenticatedUser)]
public sealed class DashboardController(IDashboardService dashboardService) : ControllerBase
{
    [HttpGet("summary")]
    [ProducesResponseType<DashboardSummaryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<DashboardSummaryResponse>> GetSummary(
        CancellationToken cancellationToken)
    {
        var result = await dashboardService.GetSummaryAsync(User, cancellationToken);
        return result.Status switch
        {
            TicketOperationStatus.Success when result.Summary is not null => Ok(result.Summary),
            TicketOperationStatus.Forbidden => Forbid(),
            _ => throw new InvalidOperationException("Dashboard summary returned an invalid result.")
        };
    }
}
