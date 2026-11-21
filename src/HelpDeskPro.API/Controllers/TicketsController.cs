using HelpDeskPro.API.Configuration;
using HelpDeskPro.API.DTOs;
using HelpDeskPro.API.Enums;
using HelpDeskPro.API.Interfaces;
using HelpDeskPro.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HelpDeskPro.API.Controllers;

[ApiController]
[Route("api/tickets")]
[Authorize(Policy = AuthorizationPolicies.AuthenticatedUser)]
public sealed class TicketsController(ITicketService ticketService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<TicketResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<TicketResponse>> Create(
        CreateTicketRequest request,
        CancellationToken cancellationToken)
    {
        var result = await ticketService.CreateAsync(request, User, cancellationToken);
        if (result.Status == TicketOperationStatus.InvalidRequest)
        {
            return BadRequest(InvalidTicketProblem());
        }

        if (result.Status == TicketOperationStatus.Forbidden)
        {
            return Forbid();
        }

        var ticket = result.Ticket
            ?? throw new InvalidOperationException("Ticket creation returned no ticket.");

        return CreatedAtAction(nameof(GetById), new { id = ticket.Id }, ticket);
    }

    [HttpGet]
    [ProducesResponseType<PaginatedResponse<TicketResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PaginatedResponse<TicketResponse>>> GetAll(
        [FromQuery] TicketQueryParameters query,
        CancellationToken cancellationToken)
    {
        var result = await ticketService.GetAllAsync(query, User, cancellationToken);
        return result.Status switch
        {
            TicketOperationStatus.Success when result.Page is not null => Ok(result.Page),
            TicketOperationStatus.InvalidRequest => BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid ticket query",
                Detail = "One or more search, filter, sorting, or pagination values are invalid."
            }),
            TicketOperationStatus.Forbidden => Forbid(),
            _ => throw new InvalidOperationException("Ticket listing returned an invalid result.")
        };
    }

    [HttpGet("overdue")]
    [ProducesResponseType<IReadOnlyList<TicketResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TicketResponse>>> GetOverdue(
        CancellationToken cancellationToken)
    {
        var tickets = await ticketService.GetOverdueAsync(User, cancellationToken);
        return Ok(tickets);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType<TicketResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TicketResponse>> GetById(
        int id,
        CancellationToken cancellationToken)
    {
        var ticket = await ticketService.GetByIdAsync(id, User, cancellationToken);
        return ticket is null ? NotFound() : Ok(ticket);
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType<TicketResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TicketResponse>> Update(
        int id,
        UpdateTicketRequest request,
        CancellationToken cancellationToken)
    {
        var result = await ticketService.UpdateAsync(id, request, User, cancellationToken);
        return result.Status switch
        {
            TicketOperationStatus.Success when result.Ticket is not null => Ok(result.Ticket),
            TicketOperationStatus.InvalidRequest => BadRequest(InvalidTicketProblem()),
            TicketOperationStatus.Forbidden => Forbid(),
            TicketOperationStatus.NotFound => NotFound(),
            _ => throw new InvalidOperationException("Ticket update returned an invalid result.")
        };
    }

    [HttpPut("{id:int}/status")]
    [Authorize(Policy = AuthorizationPolicies.AgentOrAdmin)]
    [ProducesResponseType<TicketResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TicketResponse>> UpdateStatus(
        int id,
        UpdateTicketStatusRequest request,
        CancellationToken cancellationToken)
    {
        var result = await ticketService.UpdateStatusAsync(id, request, User, cancellationToken);
        return result.Status switch
        {
            TicketOperationStatus.Success when result.Ticket is not null => Ok(result.Ticket),
            TicketOperationStatus.InvalidRequest => BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid ticket status transition",
                Detail = "The requested status is invalid or is not an allowed transition."
            }),
            TicketOperationStatus.Forbidden => Forbid(),
            TicketOperationStatus.NotFound => NotFound(),
            _ => throw new InvalidOperationException("Ticket status update returned an invalid result.")
        };
    }

    [HttpPost("{id:int}/comments")]
    [ProducesResponseType<TicketCommentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TicketCommentResponse>> CreateComment(
        int id,
        CreateTicketCommentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await ticketService.CreateCommentAsync(id, request, User, cancellationToken);
        return result.Status switch
        {
            TicketOperationStatus.Success when result.Comment is not null =>
                CreatedAtAction(nameof(GetComments), new { id }, result.Comment),
            TicketOperationStatus.InvalidRequest => BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid ticket comment",
                Detail = "Comment text must contain non-whitespace characters and be at most 4000 characters."
            }),
            TicketOperationStatus.Forbidden => Forbid(),
            TicketOperationStatus.NotFound => NotFound(),
            _ => throw new InvalidOperationException("Ticket comment creation returned an invalid result.")
        };
    }

    [HttpGet("{id:int}/comments")]
    [ProducesResponseType<IReadOnlyList<TicketCommentResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<TicketCommentResponse>>> GetComments(
        int id,
        CancellationToken cancellationToken)
    {
        var comments = await ticketService.GetCommentsAsync(id, User, cancellationToken);
        return comments is null ? NotFound() : Ok(comments);
    }

    [HttpGet("{id:int}/history")]
    [ProducesResponseType<IReadOnlyList<TicketHistoryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<TicketHistoryResponse>>> GetHistory(
        int id,
        CancellationToken cancellationToken)
    {
        var history = await ticketService.GetHistoryAsync(id, User, cancellationToken);
        return history is null ? NotFound() : Ok(history);
    }

    [HttpPut("{id:int}/assignment")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [ProducesResponseType<TicketAssignmentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TicketAssignmentResponse>> Assign(
        int id,
        AssignTicketRequest request,
        CancellationToken cancellationToken)
    {
        var result = await ticketService.AssignAsync(id, request, User, cancellationToken);
        return result.Status switch
        {
            TicketOperationStatus.Success when result.Assignment is not null =>
                Ok(result.Assignment),
            TicketOperationStatus.InvalidRequest => BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid agent assignment",
                Detail = "The target must be an active Agent account."
            }),
            TicketOperationStatus.Forbidden => Forbid(),
            TicketOperationStatus.NotFound => NotFound(),
            _ => throw new InvalidOperationException("Ticket assignment returned an invalid result.")
        };
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(
        int id,
        CancellationToken cancellationToken)
    {
        var result = await ticketService.DeleteAsync(id, User, cancellationToken);
        return result.Status switch
        {
            TicketOperationStatus.Success => NoContent(),
            TicketOperationStatus.Forbidden => Forbid(),
            TicketOperationStatus.NotFound => NotFound(),
            TicketOperationStatus.Conflict => Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Ticket cannot be deleted",
                Detail = "The ticket has related records and cannot be deleted."
            }),
            _ => throw new InvalidOperationException("Ticket deletion returned an invalid result.")
        };
    }

    private static ProblemDetails InvalidTicketProblem() =>
        new()
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Invalid ticket",
            Detail = "The ticket fields or category are invalid."
        };
}
