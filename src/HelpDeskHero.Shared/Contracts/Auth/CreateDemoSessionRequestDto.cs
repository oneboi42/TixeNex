using System.ComponentModel.DataAnnotations;

namespace HelpDeskHero.Shared.Contracts.Auth;

public sealed class CreateDemoSessionRequestDto
{
    [Required]
    [MaxLength(30)]
    public string Role { get; set; } = "User";

    [MaxLength(200)]
    public string DeviceName { get; set; } = "Demo browser";
}