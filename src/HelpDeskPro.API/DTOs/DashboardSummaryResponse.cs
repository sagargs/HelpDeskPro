namespace HelpDeskPro.API.DTOs;

/// <summary>Contains ticket metrics visible to the authenticated user.</summary>
public sealed record DashboardSummaryResponse(
    int TotalTicketCount,
    IReadOnlyList<TicketStatusCountResponse> CountsByStatus,
    IReadOnlyList<TicketPriorityCountResponse> CountsByPriority,
    int OverdueTicketCount,
    IReadOnlyList<AgentWorkloadResponse> AgentWorkloads);
