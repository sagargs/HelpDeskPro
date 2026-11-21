using System.ComponentModel.DataAnnotations;

namespace HelpDeskPro.API.DTOs;

/// <summary>Contains the text of a new ticket comment.</summary>
public sealed record CreateTicketCommentRequest
{
    [Required]
    [StringLength(4000)]
    public required string CommentText { get; init; }
}
