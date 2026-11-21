using System.ComponentModel.DataAnnotations;
using HelpDeskPro.API.Enums;

namespace HelpDeskPro.API.DTOs;

/// <summary>Contains the editable fields for a ticket.</summary>
public sealed record UpdateTicketRequest
{
    [Required]
    [StringLength(200)]
    public required string Title { get; init; }

    [Required]
    [StringLength(4000)]
    public required string Description { get; init; }

    [Range(1, int.MaxValue)]
    public required int CategoryId { get; init; }

    [EnumDataType(typeof(TicketPriority))]
    public required TicketPriority Priority { get; init; }

    [EnumDataType(typeof(TicketStatus))]
    public TicketStatus? Status { get; init; }
}
