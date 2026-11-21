using System.Security.Claims;
using HelpDeskPro.API.Services;

namespace HelpDeskPro.API.Interfaces;

public interface IDashboardService
{
    Task<DashboardSummaryResult> GetSummaryAsync(
        ClaimsPrincipal caller,
        CancellationToken cancellationToken);
}
