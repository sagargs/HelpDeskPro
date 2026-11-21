using System.ComponentModel.DataAnnotations;
using HelpDeskPro.API.Enums;

namespace HelpDeskPro.API.DTOs;

/// <summary>Requests a controlled transition to a new ticket status.</summary>
public sealed record UpdateTicketStatusRequest
{
    [EnumDataType(typeof(TicketStatus))]
    public required TicketStatus Status { get; init; }
}
