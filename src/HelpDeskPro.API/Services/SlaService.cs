using HelpDeskPro.API.Enums;
using HelpDeskPro.API.Interfaces;
using HelpDeskPro.API.Models;

namespace HelpDeskPro.API.Services;

public sealed class SlaService(TimeProvider timeProvider) : ISlaService
{
    public DateTimeOffset GetUtcNow() => timeProvider.GetUtcNow().ToUniversalTime();

    public TimeSpan GetSlaDuration(TicketPriority priority) =>
        priority switch
        {
            TicketPriority.Low => TimeSpan.FromHours(72),
            TicketPriority.Medium => TimeSpan.FromHours(48),
            TicketPriority.High => TimeSpan.FromHours(24),
            TicketPriority.Critical => TimeSpan.FromHours(4),
            _ => throw new ArgumentOutOfRangeException(
                nameof(priority),
                priority,
                "The ticket priority has no configured SLA duration.")
        };

    public DateTime CalculateDueAtUtc(TicketPriority priority, DateTimeOffset startAtUtc) =>
        startAtUtc.ToUniversalTime()
            .Add(GetSlaDuration(priority))
            .UtcDateTime;

    public bool IsOverdue(Ticket ticket, DateTimeOffset? asOfUtc = null)
    {
        if (!ticket.DueAtUtc.HasValue
            || ticket.Status is TicketStatus.Resolved or TicketStatus.Closed)
        {
            return false;
        }

        var dueAtUtc = DateTime.SpecifyKind(ticket.DueAtUtc.Value, DateTimeKind.Utc);
        return (asOfUtc ?? GetUtcNow()).ToUniversalTime().UtcDateTime > dueAtUtc;
    }

    public IQueryable<Ticket> GetOverdueTickets(
        IQueryable<Ticket> tickets,
        DateTimeOffset? asOfUtc = null) =>
        FilterByOverdue(tickets, true, asOfUtc);

    public IQueryable<Ticket> FilterByOverdue(
        IQueryable<Ticket> tickets,
        bool isOverdue,
        DateTimeOffset? asOfUtc = null)
    {
        var nowUtc = (asOfUtc ?? GetUtcNow()).ToUniversalTime().UtcDateTime;
        return isOverdue
            ? tickets.Where(ticket =>
            ticket.DueAtUtc.HasValue
            && ticket.DueAtUtc.Value < nowUtc
            && ticket.Status != TicketStatus.Resolved
            && ticket.Status != TicketStatus.Closed)
            : tickets.Where(ticket =>
                !ticket.DueAtUtc.HasValue
                || ticket.DueAtUtc.Value >= nowUtc
                || ticket.Status == TicketStatus.Resolved
                || ticket.Status == TicketStatus.Closed);
    }
}
