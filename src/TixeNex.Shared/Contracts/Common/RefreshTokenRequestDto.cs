namespace TixeNex.Shared.Contracts.Auth;

public sealed class RefreshTokenRequestDto
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
}