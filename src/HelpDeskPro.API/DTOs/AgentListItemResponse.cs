namespace HelpDeskPro.API.DTOs;

/// <summary>Contains safe identifying information for an active Agent.</summary>
public sealed record AgentListItemResponse(
    int UserId,
    string FullName,
    string Email);
