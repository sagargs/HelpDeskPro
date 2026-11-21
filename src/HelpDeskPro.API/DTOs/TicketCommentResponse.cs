namespace HelpDeskPro.API.DTOs;

/// <summary>Represents a ticket comment with safe author details.</summary>
public sealed record TicketCommentResponse(
    int Id,
    int TicketId,
    int AuthorUserId,
    string AuthorFullName,
    string CommentText,
    DateTimeOffset CreatedAtUtc);
