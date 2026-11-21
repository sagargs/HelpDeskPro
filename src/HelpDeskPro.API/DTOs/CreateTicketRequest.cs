using System.ComponentModel.DataAnnotations;
using HelpDeskPro.API.Enums;

namespace HelpDeskPro.API.DTOs;

/// <summary>Contains the fields required to create a ticket.</summary>
public sealed record CreateTicketRequest
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
}
