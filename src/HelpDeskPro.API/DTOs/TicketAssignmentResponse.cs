namespace HelpDeskPro.API.DTOs;

/// <summary>Describes the current assignment of a ticket.</summary>
public sealed record TicketAssignmentResponse(
    int TicketId,
    string TicketTitle,
    int? AssignedAgentUserId,
    string? AssignedAgentName);
