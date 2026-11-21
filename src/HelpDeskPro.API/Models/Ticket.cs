using HelpDeskPro.API.Enums;

namespace HelpDeskPro.API.Models;

public class Ticket
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public int CategoryId { get; set; }

    public int CreatedByUserId { get; set; }

    public int? AssignedToUserId { get; set; }

    public TicketPriority Priority { get; set; }

    public TicketStatus Status { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? DueAtUtc { get; set; }

    public Category Category { get; set; } = null!;

    public User CreatedByUser { get; set; } = null!;

    public User? AssignedToUser { get; set; }

    public ICollection<TicketHistory> HistoryRecords { get; set; } = new List<TicketHistory>();

    public ICollection<TicketComment> Comments { get; set; } = new List<TicketComment>();
}
