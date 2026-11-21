using System.ComponentModel.DataAnnotations;
using HelpDeskPro.API.Enums;

namespace HelpDeskPro.API.DTOs;

/// <summary>Defines search, filters, sorting, and pagination for ticket listings.</summary>
public sealed record TicketQueryParameters
{
    [StringLength(200)]
    public string? Search { get; init; }

    [EnumDataType(typeof(TicketStatus))]
    public TicketStatus? Status { get; init; }

    [EnumDataType(typeof(TicketPriority))]
    public TicketPriority? Priority { get; init; }

    [Range(1, int.MaxValue)]
    public int? CategoryId { get; init; }

    [Range(1, int.MaxValue)]
    public int? AssignedToUserId { get; init; }

    [Range(1, int.MaxValue)]
    public int? CreatedByUserId { get; init; }

    public bool? IsOverdue { get; init; }

    public string SortBy { get; init; } = "createdAtUtc";

    public string SortDirection { get; init; } = "desc";

    [Range(1, int.MaxValue)]
    public int PageNumber { get; init; } = 1;

    [Range(1, 100)]
    public int PageSize { get; init; } = 10;
}
