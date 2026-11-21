namespace HelpDeskPro.API.DTOs;

/// <summary>Specifies the Agent to assign to a ticket, or null to unassign it.</summary>
public sealed record AssignTicketRequest
{
    public required int? AgentUserId { get; init; }
}
