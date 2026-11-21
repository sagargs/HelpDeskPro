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

public sealed class TicketService(
    ApplicationDbContext dbContext,
    ISlaService slaService) : ITicketService
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

        var now = slaService.GetUtcNow();
        var ticket = new Ticket
        {
            Title = request.Title.Trim(),
            Description = request.Description.Trim(),
            CategoryId = category.Id,
            Category = category,
            CreatedByUserId = userId,
            Priority = request.Priority,
            Status = TicketStatus.Open,
            CreatedAtUtc = now.UtcDateTime,
            UpdatedAtUtc = now.UtcDateTime,
            DueAtUtc = slaService.CalculateDueAtUtc(request.Priority, now)
        };

        dbContext.Tickets.Add(ticket);
        AddHistory(
            ticket,
            userId,
            "Ticket created with status Open.",
            newStatus: TicketStatus.Open);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new TicketOperationResult(
            TicketOperationStatus.Success,
            ToResponse(ticket));
    }

    public async Task<TicketListOperationResult> GetAllAsync(
        TicketQueryParameters parameters,
        ClaimsPrincipal caller,
        CancellationToken cancellationToken)
    {
        if (!TryGetCaller(caller, out var userId, out var role))
        {
            return new TicketListOperationResult(TicketOperationStatus.Forbidden);
        }

        if (!IsValidTicketQuery(parameters))
        {
            return new TicketListOperationResult(TicketOperationStatus.InvalidRequest);
        }

        var asOfUtc = slaService.GetUtcNow();
        IQueryable<Ticket> query = GetAccessibleTickets(userId, role);
        var search = parameters.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(ticket =>
                ticket.Title.Contains(search)
                || ticket.Description.Contains(search));
        }

        if (parameters.Status.HasValue)
        {
            query = query.Where(ticket => ticket.Status == parameters.Status.Value);
        }

        if (parameters.Priority.HasValue)
        {
            query = query.Where(ticket => ticket.Priority == parameters.Priority.Value);
        }

        if (parameters.CategoryId.HasValue)
        {
            query = query.Where(ticket => ticket.CategoryId == parameters.CategoryId.Value);
        }

        if (parameters.AssignedToUserId.HasValue)
        {
            query = query.Where(ticket =>
                ticket.AssignedToUserId == parameters.AssignedToUserId.Value);
        }

        if (parameters.CreatedByUserId.HasValue)
        {
            query = query.Where(ticket =>
                ticket.CreatedByUserId == parameters.CreatedByUserId.Value);
        }

        if (parameters.IsOverdue.HasValue)
        {
            query = slaService.FilterByOverdue(query, parameters.IsOverdue.Value, asOfUtc);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = totalCount == 0
            ? 0
            : (int)(((long)totalCount + parameters.PageSize - 1) / parameters.PageSize);
        var skip = (long)(parameters.PageNumber - 1) * parameters.PageSize;
        IReadOnlyList<TicketResponse> items = Array.Empty<TicketResponse>();
        if (skip < totalCount)
        {
            var tickets = await ApplySorting(
                    query,
                    parameters.SortBy,
                    parameters.SortDirection)
                .AsNoTracking()
                .Include(ticket => ticket.Category)
                .Skip((int)skip)
                .Take(parameters.PageSize)
                .ToListAsync(cancellationToken);
            items = tickets.Select(ticket => ToResponse(ticket, asOfUtc)).ToArray();
        }

        return new TicketListOperationResult(
            TicketOperationStatus.Success,
            new PaginatedResponse<TicketResponse>(
                items,
                parameters.PageNumber,
                parameters.PageSize,
                totalCount,
                totalPages,
                parameters.PageNumber > 1,
                parameters.PageNumber < totalPages));
    }

    public async Task<IReadOnlyList<TicketResponse>> GetOverdueAsync(
        ClaimsPrincipal caller,
        CancellationToken cancellationToken)
    {
        if (!TryGetCaller(caller, out var userId, out var role))
        {
            return Array.Empty<TicketResponse>();
        }

        var asOfUtc = slaService.GetUtcNow();
        var tickets = await slaService.GetOverdueTickets(
                GetAccessibleTickets(userId, role),
                asOfUtc)
            .AsNoTracking()
            .Include(ticket => ticket.Category)
            .ToListAsync(cancellationToken);

        return tickets.Select(ticket => ToResponse(ticket, asOfUtc)).ToArray();
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

        var title = request.Title.Trim();
        var description = request.Description.Trim();
        var changedFields = new List<string>();
        if (!string.Equals(ticket.Title, title, StringComparison.Ordinal))
        {
            changedFields.Add(nameof(ticket.Title));
        }

        if (!string.Equals(ticket.Description, description, StringComparison.Ordinal))
        {
            changedFields.Add(nameof(ticket.Description));
        }

        if (ticket.CategoryId != category.Id)
        {
            changedFields.Add(nameof(ticket.CategoryId));
        }

        if (ticket.Priority != request.Priority)
        {
            changedFields.Add(nameof(ticket.Priority));
        }

        var priorityChanged = ticket.Priority != request.Priority;
        var now = slaService.GetUtcNow();
        ticket.Title = title;
        ticket.Description = description;
        ticket.CategoryId = category.Id;
        ticket.Category = category;
        ticket.Priority = request.Priority;
        if (priorityChanged)
        {
            ticket.DueAtUtc = slaService.CalculateDueAtUtc(request.Priority, now);
        }

        ticket.UpdatedAtUtc = now.UtcDateTime;
        if (changedFields.Count > 0)
        {
            AddHistory(
                ticket,
                userId,
                $"Ticket updated. Changed fields: {string.Join(", ", changedFields)}.");
        }

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

    public async Task<TicketAssignmentOperationResult> AssignAsync(
        int id,
        AssignTicketRequest request,
        ClaimsPrincipal caller,
        CancellationToken cancellationToken)
    {
        if (!TryGetCaller(caller, out var userId, out var role)
            || role != UserRole.Admin)
        {
            return new TicketAssignmentOperationResult(TicketOperationStatus.Forbidden);
        }

        if (request.AgentUserId is <= 0)
        {
            return new TicketAssignmentOperationResult(TicketOperationStatus.InvalidRequest);
        }

        var ticket = await dbContext.Tickets
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (ticket is null)
        {
            return new TicketAssignmentOperationResult(TicketOperationStatus.NotFound);
        }

        User? agent = null;
        if (request.AgentUserId.HasValue)
        {
            agent = await dbContext.Users
                .SingleOrDefaultAsync(
                    user => user.Id == request.AgentUserId.Value,
                    cancellationToken);
            if (agent is null)
            {
                return new TicketAssignmentOperationResult(TicketOperationStatus.NotFound);
            }

            if (agent.Role != UserRole.Agent || !agent.IsActive)
            {
                return new TicketAssignmentOperationResult(TicketOperationStatus.InvalidRequest);
            }
        }

        var previousAgentId = ticket.AssignedToUserId;
        if (previousAgentId != agent?.Id)
        {
            ticket.AssignedToUserId = agent?.Id;
            ticket.UpdatedAtUtc = slaService.GetUtcNow().UtcDateTime;
            AddHistory(
                ticket,
                userId,
                GetAssignmentAction(previousAgentId, agent?.Id));
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new TicketAssignmentOperationResult(
            TicketOperationStatus.Success,
            new TicketAssignmentResponse(
                ticket.Id,
                ticket.Title,
                agent?.Id,
                agent?.FullName));
    }

    public async Task<TicketOperationResult> UpdateStatusAsync(
        int id,
        UpdateTicketStatusRequest request,
        ClaimsPrincipal caller,
        CancellationToken cancellationToken)
    {
        if (!TryGetCaller(caller, out var userId, out var role)
            || role is not (UserRole.Admin or UserRole.Agent))
        {
            return new TicketOperationResult(TicketOperationStatus.Forbidden);
        }

        if (!Enum.IsDefined(request.Status))
        {
            return new TicketOperationResult(TicketOperationStatus.InvalidRequest);
        }

        var ticket = await GetStatusAccessibleTickets(userId, role)
            .Include(item => item.Category)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (ticket is null)
        {
            return new TicketOperationResult(TicketOperationStatus.NotFound);
        }

        if (!IsAllowedStatusTransition(ticket.Status, request.Status))
        {
            return new TicketOperationResult(TicketOperationStatus.InvalidRequest);
        }

        var oldStatus = ticket.Status;
        ticket.Status = request.Status;
        ticket.UpdatedAtUtc = slaService.GetUtcNow().UtcDateTime;
        AddHistory(
            ticket,
            userId,
            $"Ticket status changed from {oldStatus} to {request.Status}.",
            oldStatus,
            request.Status);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new TicketOperationResult(
            TicketOperationStatus.Success,
            ToResponse(ticket));
    }

    public async Task<TicketCommentOperationResult> CreateCommentAsync(
        int id,
        CreateTicketCommentRequest request,
        ClaimsPrincipal caller,
        CancellationToken cancellationToken)
    {
        if (!TryGetCaller(caller, out var userId, out var role))
        {
            return new TicketCommentOperationResult(TicketOperationStatus.Forbidden);
        }

        if (string.IsNullOrWhiteSpace(request.CommentText)
            || request.CommentText.Trim().Length > 4000)
        {
            return new TicketCommentOperationResult(TicketOperationStatus.InvalidRequest);
        }

        var ticket = await GetParticipatingTickets(id, userId, role)
            .SingleOrDefaultAsync(cancellationToken);
        if (ticket is null)
        {
            return new TicketCommentOperationResult(TicketOperationStatus.NotFound);
        }

        var author = await dbContext.Users
            .SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);
        if (author is null)
        {
            return new TicketCommentOperationResult(TicketOperationStatus.Forbidden);
        }

        var comment = new TicketComment
        {
            Ticket = ticket,
            User = author,
            UserId = userId,
            CommentText = request.CommentText.Trim(),
            CreatedAtUtc = slaService.GetUtcNow().UtcDateTime
        };
        dbContext.TicketComments.Add(comment);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new TicketCommentOperationResult(
            TicketOperationStatus.Success,
            ToCommentResponse(comment));
    }

    public async Task<IReadOnlyList<TicketCommentResponse>?> GetCommentsAsync(
        int id,
        ClaimsPrincipal caller,
        CancellationToken cancellationToken)
    {
        if (!TryGetCaller(caller, out var userId, out var role))
        {
            return null;
        }

        var ticketExists = await GetParticipatingTickets(id, userId, role)
            .AnyAsync(cancellationToken);
        if (!ticketExists)
        {
            return null;
        }

        var comments = await dbContext.TicketComments
            .AsNoTracking()
            .Include(comment => comment.User)
            .Where(comment => comment.TicketId == id)
            .OrderBy(comment => comment.CreatedAtUtc)
            .ThenBy(comment => comment.Id)
            .ToListAsync(cancellationToken);

        return comments.Select(ToCommentResponse).ToArray();
    }

    public async Task<IReadOnlyList<TicketHistoryResponse>?> GetHistoryAsync(
        int id,
        ClaimsPrincipal caller,
        CancellationToken cancellationToken)
    {
        if (!TryGetCaller(caller, out var userId, out var role))
        {
            return null;
        }

        var ticketExists = await GetParticipatingTickets(id, userId, role)
            .AnyAsync(cancellationToken);
        if (!ticketExists)
        {
            return null;
        }

        var history = await dbContext.TicketHistories
            .AsNoTracking()
            .Include(item => item.ChangedByUser)
            .Where(item => item.TicketId == id)
            .OrderBy(item => item.ChangedAtUtc)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);

        return history.Select(ToHistoryResponse).ToArray();
    }

    private IQueryable<Ticket> GetAccessibleTickets(int userId, UserRole role) =>
        role switch
        {
            UserRole.Admin => dbContext.Tickets,
            UserRole.Agent => dbContext.Tickets.Where(ticket => ticket.AssignedToUserId == userId),
            UserRole.Employee => dbContext.Tickets.Where(ticket => ticket.CreatedByUserId == userId),
            _ => dbContext.Tickets.Where(_ => false)
        };

    private static bool IsValidTicketQuery(TicketQueryParameters parameters) =>
        parameters.PageNumber >= 1
        && parameters.PageSize is >= 1 and <= 100
        && (!parameters.CategoryId.HasValue || parameters.CategoryId.Value > 0)
        && (!parameters.AssignedToUserId.HasValue || parameters.AssignedToUserId.Value > 0)
        && (!parameters.CreatedByUserId.HasValue || parameters.CreatedByUserId.Value > 0)
        && (!parameters.Status.HasValue || Enum.IsDefined(parameters.Status.Value))
        && (!parameters.Priority.HasValue || Enum.IsDefined(parameters.Priority.Value))
        && (parameters.Search is null || parameters.Search.Length <= 200)
        && IsValidSort(parameters.SortBy, parameters.SortDirection);

    private static bool IsValidSort(string sortBy, string sortDirection) =>
        !string.IsNullOrWhiteSpace(sortBy)
        && !string.IsNullOrWhiteSpace(sortDirection)
        && (sortBy.Equals("createdAtUtc", StringComparison.OrdinalIgnoreCase)
            || sortBy.Equals("updatedAtUtc", StringComparison.OrdinalIgnoreCase)
            || sortBy.Equals("dueAtUtc", StringComparison.OrdinalIgnoreCase)
            || sortBy.Equals("priority", StringComparison.OrdinalIgnoreCase)
            || sortBy.Equals("status", StringComparison.OrdinalIgnoreCase))
        && (sortDirection.Equals("asc", StringComparison.OrdinalIgnoreCase)
            || sortDirection.Equals("desc", StringComparison.OrdinalIgnoreCase));

    private static IOrderedQueryable<Ticket> ApplySorting(
        IQueryable<Ticket> query,
        string sortBy,
        string sortDirection)
    {
        var descending = sortDirection.Equals("desc", StringComparison.OrdinalIgnoreCase);
        var orderedQuery = sortBy.ToLowerInvariant() switch
        {
            "createdatutc" => descending
                ? query.OrderByDescending(ticket => ticket.CreatedAtUtc)
                : query.OrderBy(ticket => ticket.CreatedAtUtc),
            "updatedatutc" => descending
                ? query.OrderByDescending(ticket => ticket.UpdatedAtUtc)
                : query.OrderBy(ticket => ticket.UpdatedAtUtc),
            "dueatutc" => descending
                ? query.OrderByDescending(ticket => ticket.DueAtUtc)
                : query.OrderBy(ticket => ticket.DueAtUtc),
            "priority" => descending
                ? query.OrderByDescending(ticket => ticket.Priority)
                : query.OrderBy(ticket => ticket.Priority),
            "status" => descending
                ? query.OrderByDescending(ticket => ticket.Status)
                : query.OrderBy(ticket => ticket.Status),
            _ => throw new InvalidOperationException("Ticket sorting was not validated.")
        };

        return orderedQuery.ThenBy(ticket => ticket.Id);
    }

    private IQueryable<Ticket> GetStatusAccessibleTickets(int userId, UserRole role) =>
        dbContext.Tickets.Where(ticket =>
            role == UserRole.Admin
            || (role == UserRole.Agent && ticket.AssignedToUserId == userId));

    private IQueryable<Ticket> GetParticipatingTickets(
        int id,
        int userId,
        UserRole role) =>
        dbContext.Tickets.Where(ticket =>
            ticket.Id == id
            && (role == UserRole.Admin
                || ticket.CreatedByUserId == userId
                || (role == UserRole.Agent && ticket.AssignedToUserId == userId)));

    private void AddHistory(
        Ticket ticket,
        int actorUserId,
        string action,
        TicketStatus? oldStatus = null,
        TicketStatus? newStatus = null) =>
        dbContext.TicketHistories.Add(new TicketHistory
        {
            Ticket = ticket,
            ChangedByUserId = actorUserId,
            OldStatus = oldStatus,
            NewStatus = newStatus,
            Action = action,
            ChangedAtUtc = slaService.GetUtcNow().UtcDateTime
        });

    private static bool IsAllowedStatusTransition(TicketStatus current, TicketStatus next) =>
        current switch
        {
            TicketStatus.Open => next == TicketStatus.InProgress,
            TicketStatus.InProgress => next == TicketStatus.Resolved,
            TicketStatus.Resolved => next is TicketStatus.InProgress or TicketStatus.Closed,
            _ => false
        };

    private static string GetAssignmentAction(int? previousAgentId, int? newAgentId) =>
        (previousAgentId, newAgentId) switch
        {
            (null, int assignedId) => $"Ticket assigned to Agent user {assignedId}.",
            (int unassignedId, null) => $"Ticket unassigned from Agent user {unassignedId}.",
            (int oldAgentId, int newAssignedId) =>
                $"Ticket reassigned from Agent user {oldAgentId} to Agent user {newAssignedId}.",
            _ => throw new InvalidOperationException("The assignment did not change.")
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

    private TicketResponse ToResponse(Ticket ticket) =>
        ToResponse(ticket, slaService.GetUtcNow());

    private TicketResponse ToResponse(Ticket ticket, DateTimeOffset asOfUtc) =>
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
            ticket.DueAtUtc.HasValue ? ToUtcOffset(ticket.DueAtUtc.Value) : null,
            slaService.IsOverdue(ticket, asOfUtc));

    private static TicketCommentResponse ToCommentResponse(TicketComment comment) =>
        new(
            comment.Id,
            comment.TicketId,
            comment.UserId,
            comment.User.FullName,
            comment.CommentText,
            ToUtcOffset(comment.CreatedAtUtc));

    private static TicketHistoryResponse ToHistoryResponse(TicketHistory history) =>
        new(
            history.Id,
            history.TicketId,
            history.ChangedByUserId,
            history.ChangedByUser.FullName,
            history.OldStatus,
            history.NewStatus,
            history.Action,
            ToUtcOffset(history.ChangedAtUtc));

    private static DateTimeOffset ToUtcOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static bool IsForeignKeyViolation(DbUpdateException exception) =>
        exception.GetBaseException() is SqlException { Number: 547 };
}
