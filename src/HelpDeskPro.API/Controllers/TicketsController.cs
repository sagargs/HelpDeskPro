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
    [ProducesResponseType<IReadOnlyList<TicketResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TicketResponse>>> GetAll(
        CancellationToken cancellationToken)
    {
        var tickets = await ticketService.GetAllAsync(User, cancellationToken);
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
