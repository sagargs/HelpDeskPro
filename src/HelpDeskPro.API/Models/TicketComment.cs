namespace HelpDeskPro.API.Models;

public class TicketComment
{
    public int Id { get; set; }

    public int TicketId { get; set; }

    public int UserId { get; set; }

    public string CommentText { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public Ticket Ticket { get; set; } = null!;

    public User User { get; set; } = null!;
}
