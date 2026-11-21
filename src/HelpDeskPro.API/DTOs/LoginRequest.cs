using System.ComponentModel.DataAnnotations;

namespace HelpDeskPro.API.DTOs;

/// <summary>Contains the credentials required to log in.</summary>
public sealed record LoginRequest
{
    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string Email { get; init; } = string.Empty;

    [Required]
    public string Password { get; init; } = string.Empty;
}
