namespace HelpDeskPro.API.DTOs;

/// <summary>Summarizes the active and overdue assignments for an Agent.</summary>
public sealed record AgentWorkloadResponse(
    int AgentUserId,
    string AgentName,
    int ActiveAssignedTicketCount,
    int OverdueAssignedTicketCount);
