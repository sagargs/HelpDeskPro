using HelpDeskPro.API.DTOs;

namespace HelpDeskPro.API.Services;

public sealed record DashboardSummaryResult(
    TicketOperationStatus Status,
    DashboardSummaryResponse? Summary = null);
