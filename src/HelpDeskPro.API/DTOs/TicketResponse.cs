using HelpDeskPro.API.Enums;

namespace HelpDeskPro.API.DTOs;

/// <summary>Represents a ticket returned by the API.</summary>
public sealed record TicketResponse(
    int Id,
    string Title,
    string Description,
    int CategoryId,
    string CategoryName,
    int CreatedByUserId,
    int? AssignedToUserId,
    TicketPriority Priority,
    TicketStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? DueAtUtc,
    bool IsOverdue);
