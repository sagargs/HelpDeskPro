using HelpDeskPro.API.DTOs;

namespace HelpDeskPro.API.Services;

public enum TicketOperationStatus
{
    Success,
    InvalidRequest,
    NotFound,
    Forbidden,
    Conflict
}

public sealed record TicketOperationResult(
    TicketOperationStatus Status,
    TicketResponse? Ticket = null);

public sealed record TicketAssignmentOperationResult(
    TicketOperationStatus Status,
    TicketAssignmentResponse? Assignment = null);
