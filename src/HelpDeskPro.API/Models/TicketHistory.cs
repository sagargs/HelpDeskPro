using HelpDeskPro.API.Enums;

namespace HelpDeskPro.API.Models;

public class TicketHistory
{
    public int Id { get; set; }

    public int TicketId { get; set; }

    public int ChangedByUserId { get; set; }

    public TicketStatus? OldStatus { get; set; }

    public TicketStatus? NewStatus { get; set; }

    public string Action { get; set; } = string.Empty;

    public DateTime ChangedAtUtc { get; set; }

    public Ticket Ticket { get; set; } = null!;

    public User ChangedByUser { get; set; } = null!;
}
