using HelpDeskPro.API.Enums;

namespace HelpDeskPro.API.DTOs;

/// <summary>Contains the ticket count for one status.</summary>
public sealed record TicketStatusCountResponse(TicketStatus Status, int Count);
