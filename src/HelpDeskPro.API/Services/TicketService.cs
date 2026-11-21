using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using HelpDeskPro.API.Configuration;
using HelpDeskPro.API.Data;
using HelpDeskPro.API.DTOs;
using HelpDeskPro.API.Enums;
using HelpDeskPro.API.Interfaces;
using HelpDeskPro.API.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskPro.API.Services;

public sealed class TicketService(ApplicationDbContext dbContext) : ITicketService
{
    public async Task<TicketOperationResult> CreateAsync(
        CreateTicketRequest request,
        ClaimsPrincipal caller,
        CancellationToken cancellationToken)
    {
        if (!TryGetCaller(caller, out var userId, out _))
        {
            return new TicketOperationResult(TicketOperationStatus.Forbidden);
        }

        if (!IsValidTicketText(request.Title, request.Description)
            || !Enum.IsDefined(request.Priority))
        {
            return new TicketOperationResult(TicketOperationStatus.InvalidRequest);
        }

        var category = await dbContext.Categories
            .SingleOrDefaultAsync(
                item => item.Id == request.CategoryId && item.IsActive,
                cancellationToken);
        if (category is null)
        {
            return new TicketOperationResult(TicketOperationStatus.InvalidRequest);
        }

        var now = DateTime.UtcNow;
        var ticket = new Ticket
        {
            Title = request.Title.Trim(),
            Description = request.Description.Trim(),
            CategoryId = category.Id,
            Category = category,
            CreatedByUserId = userId,
            Priority = request.Priority,
            Status = TicketStatus.Open,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        dbContext.Tickets.Add(ticket);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new TicketOperationResult(
            TicketOperationStatus.Success,
            ToResponse(ticket));
    }

    public async Task<IReadOnlyList<TicketResponse>> GetAllAsync(
        ClaimsPrincipal caller,
        CancellationToken cancellationToken)
    {
        if (!TryGetCaller(caller, out var userId, out var role))
        {
            return Array.Empty<TicketResponse>();
        }

        var tickets = await GetAccessibleTickets(userId, role)
            .AsNoTracking()
            .Include(ticket => ticket.Category)
            .ToListAsync(cancellationToken);

        return tickets.Select(ToResponse).ToArray();
    }

    public async Task<TicketResponse?> GetByIdAsync(
        int id,
        ClaimsPrincipal caller,
        CancellationToken cancellationToken)
    {
        if (!TryGetCaller(caller, out var userId, out var role))
        {
            return null;
        }

        var ticket = await GetAccessibleTickets(userId, role)
            .AsNoTracking()
            .Include(item => item.Category)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        return ticket is null ? null : ToResponse(ticket);
    }

    public async Task<TicketOperationResult> UpdateAsync(
        int id,
        UpdateTicketRequest request,
        ClaimsPrincipal caller,
        CancellationToken cancellationToken)
    {
        if (!TryGetCaller(caller, out var userId, out var role))
        {
            return new TicketOperationResult(TicketOperationStatus.Forbidden);
        }

        var ticket = await GetAccessibleTickets(userId, role)
            .Include(item => item.Category)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (ticket is null)
        {
            return new TicketOperationResult(TicketOperationStatus.NotFound);
        }

        if (role == UserRole.Employee && request.Status.HasValue)
        {
            return new TicketOperationResult(TicketOperationStatus.Forbidden);
        }

        if (!IsValidTicketText(request.Title, request.Description)
            || !Enum.IsDefined(request.Priority)
            || (request.Status.HasValue && !Enum.IsDefined(request.Status.Value)))
        {
            return new TicketOperationResult(TicketOperationStatus.InvalidRequest);
        }

        var category = await dbContext.Categories
            .SingleOrDefaultAsync(
                item => item.Id == request.CategoryId && item.IsActive,
                cancellationToken);
        if (category is null)
        {
            return new TicketOperationResult(TicketOperationStatus.InvalidRequest);
        }

        ticket.Title = request.Title.Trim();
        ticket.Description = request.Description.Trim();
        ticket.CategoryId = category.Id;
        ticket.Category = category;
        ticket.Priority = request.Priority;
        if (request.Status.HasValue)
        {
            ticket.Status = request.Status.Value;
        }

        ticket.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new TicketOperationResult(
            TicketOperationStatus.Success,
            ToResponse(ticket));
    }

    public async Task<TicketOperationResult> DeleteAsync(
        int id,
        ClaimsPrincipal caller,
        CancellationToken cancellationToken)
    {
        if (!TryGetCaller(caller, out _, out var role)
            || role != UserRole.Admin)
        {
            return new TicketOperationResult(TicketOperationStatus.Forbidden);
        }

        var ticket = await dbContext.Tickets
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (ticket is null)
        {
            return new TicketOperationResult(TicketOperationStatus.NotFound);
        }

        dbContext.Tickets.Remove(ticket);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsForeignKeyViolation(exception))
        {
            return new TicketOperationResult(TicketOperationStatus.Conflict);
        }

        return new TicketOperationResult(TicketOperationStatus.Success);
    }

    private IQueryable<Ticket> GetAccessibleTickets(int userId, UserRole role) =>
        role switch
        {
            UserRole.Admin => dbContext.Tickets,
            UserRole.Agent => dbContext.Tickets.Where(ticket => ticket.AssignedToUserId == userId),
            UserRole.Employee => dbContext.Tickets.Where(ticket => ticket.CreatedByUserId == userId),
            _ => dbContext.Tickets.Where(_ => false)
        };

    private static bool TryGetCaller(
        ClaimsPrincipal caller,
        out int userId,
        out UserRole role)
    {
        userId = 0;
        role = default;

        if (caller.Identity?.IsAuthenticated != true
            || !int.TryParse(
                caller.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out userId)
            || userId <= 0)
        {
            return false;
        }

        if (caller.IsInRole(nameof(UserRole.Admin)))
        {
            role = UserRole.Admin;
            return true;
        }

        if (caller.IsInRole(nameof(UserRole.Agent)))
        {
            role = UserRole.Agent;
            return true;
        }

        if (caller.IsInRole(nameof(UserRole.Employee)))
        {
            role = UserRole.Employee;
            return true;
        }

        return false;
    }

    private static bool IsValidTicketText(string title, string description) =>
        !string.IsNullOrWhiteSpace(title)
        && title.Trim().Length <= 200
        && !string.IsNullOrWhiteSpace(description)
        && description.Trim().Length <= 4000;

    private static TicketResponse ToResponse(Ticket ticket) =>
        new(
            ticket.Id,
            ticket.Title,
            ticket.Description,
            ticket.CategoryId,
            ticket.Category.Name,
            ticket.CreatedByUserId,
            ticket.AssignedToUserId,
            ticket.Priority,
            ticket.Status,
            ToUtcOffset(ticket.CreatedAtUtc),
            ToUtcOffset(ticket.UpdatedAtUtc),
            ticket.DueAtUtc.HasValue ? ToUtcOffset(ticket.DueAtUtc.Value) : null);

    private static DateTimeOffset ToUtcOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static bool IsForeignKeyViolation(DbUpdateException exception) =>
        exception.GetBaseException() is SqlException { Number: 547 };
}
