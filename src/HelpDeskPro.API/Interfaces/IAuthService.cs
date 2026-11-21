using HelpDeskPro.API.DTOs;

namespace HelpDeskPro.API.Interfaces;

public interface IAuthService
{
    Task<AuthenticationResponse?> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken);

    Task<AuthenticationResponse?> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken);
}
