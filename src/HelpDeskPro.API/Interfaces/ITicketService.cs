using System.Security.Claims;
using HelpDeskPro.API.DTOs;
using HelpDeskPro.API.Services;

namespace HelpDeskPro.API.Interfaces;

public interface ITicketService
{
    Task<TicketOperationResult> CreateAsync(
        CreateTicketRequest request,
        ClaimsPrincipal caller,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TicketResponse>> GetAllAsync(
        ClaimsPrincipal caller,
        CancellationToken cancellationToken);

    Task<TicketResponse?> GetByIdAsync(
        int id,
        ClaimsPrincipal caller,
        CancellationToken cancellationToken);

    Task<TicketOperationResult> UpdateAsync(
        int id,
        UpdateTicketRequest request,
        ClaimsPrincipal caller,
        CancellationToken cancellationToken);

    Task<TicketOperationResult> DeleteAsync(
        int id,
        ClaimsPrincipal caller,
        CancellationToken cancellationToken);

    Task<TicketAssignmentOperationResult> AssignAsync(
        int id,
        AssignTicketRequest request,
        ClaimsPrincipal caller,
        CancellationToken cancellationToken);
}
