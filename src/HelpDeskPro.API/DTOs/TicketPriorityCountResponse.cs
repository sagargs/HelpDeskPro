using HelpDeskPro.API.Enums;

namespace HelpDeskPro.API.DTOs;

/// <summary>Contains the ticket count for one priority.</summary>
public sealed record TicketPriorityCountResponse(TicketPriority Priority, int Count);
