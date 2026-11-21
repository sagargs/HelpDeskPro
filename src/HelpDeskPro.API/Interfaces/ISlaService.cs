using HelpDeskPro.API.Enums;
using HelpDeskPro.API.Models;

namespace HelpDeskPro.API.Interfaces;

public interface ISlaService
{
    DateTimeOffset GetUtcNow();

    TimeSpan GetSlaDuration(TicketPriority priority);

    DateTime CalculateDueAtUtc(TicketPriority priority, DateTimeOffset startAtUtc);

    bool IsOverdue(Ticket ticket);

    IQueryable<Ticket> GetOverdueTickets(IQueryable<Ticket> tickets);
}
