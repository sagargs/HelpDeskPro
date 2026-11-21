using HelpDeskPro.API.Enums;

namespace HelpDeskPro.API.DTOs;

/// <summary>Represents an audited event for a ticket.</summary>
public sealed record TicketHistoryResponse(
    int Id,
    int TicketId,
    int ChangedByUserId,
    string ChangedByFullName,
    TicketStatus? OldStatus,
    TicketStatus? NewStatus,
    string Action,
    DateTimeOffset ChangedAtUtc);
