using System.ComponentModel.DataAnnotations;
using System.Text;

namespace HelpDeskPro.API.DTOs;

/// <summary>Contains the public fields required to register an employee account.</summary>
public sealed record RegisterRequest : IValidatableObject
{
    [Required]
    [StringLength(200)]
    public string FullName { get; init; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string Email { get; init; } = string.Empty;

    [Required]
    public string Password { get; init; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var hasRequiredCharacterTypes =
            Password.Any(char.IsLower)
            && Password.Any(char.IsUpper)
            && Password.Any(char.IsDigit)
            && Password.Any(character => !char.IsLetterOrDigit(character));

        if (Password.Length < 12
            || Encoding.UTF8.GetByteCount(Password) > 72
            || !hasRequiredCharacterTypes)
        {
            yield return new ValidationResult(
                "Password must be at least 12 characters, include uppercase and lowercase letters, a number, and a symbol, and use no more than 72 UTF-8 bytes.",
                [nameof(Password)]);
        }
    }
}
