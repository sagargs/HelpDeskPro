using HelpDeskPro.API.Enums;
using HelpDeskPro.API.Models;

namespace HelpDeskPro.API.Interfaces;

public interface ISlaService
{
    DateTimeOffset GetUtcNow();

    TimeSpan GetSlaDuration(TicketPriority priority);

    DateTime CalculateDueAtUtc(TicketPriority priority, DateTimeOffset startAtUtc);

    bool IsOverdue(Ticket ticket, DateTimeOffset? asOfUtc = null);

    IQueryable<Ticket> GetOverdueTickets(
        IQueryable<Ticket> tickets,
        DateTimeOffset? asOfUtc = null);

    IQueryable<Ticket> FilterByOverdue(
        IQueryable<Ticket> tickets,
        bool isOverdue,
        DateTimeOffset? asOfUtc = null);
}
