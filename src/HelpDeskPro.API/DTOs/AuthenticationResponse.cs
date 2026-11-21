namespace HelpDeskPro.API.DTOs;

/// <summary>Contains an access token and safe account details after authentication.</summary>
public sealed record AuthenticationResponse(
    int UserId,
    string FullName,
    string Email,
    string Role,
    string AccessToken,
    DateTimeOffset ExpiresAtUtc);
