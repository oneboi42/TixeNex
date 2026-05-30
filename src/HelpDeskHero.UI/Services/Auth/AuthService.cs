using System.Net.Http.Json;
using HelpDeskHero.Shared.Contracts.Auth;
using Microsoft.AspNetCore.Components.Authorization;

namespace HelpDeskHero.UI.Services.Auth;

public sealed class AuthService
{
    private readonly HttpClient _http;
    private readonly TokenStorageService _tokenStorage;
    private readonly JwtAuthenticationStateProvider _authStateProvider;

    public AuthService(
        HttpClient http,
        TokenStorageService tokenStorage,
        AuthenticationStateProvider authStateProvider)
    {
        _http = http;
        _tokenStorage = tokenStorage;
        _authStateProvider = (JwtAuthenticationStateProvider)authStateProvider;
    }

    public async Task<bool> LoginAsync(string userName, string password, CancellationToken ct = default)
    {
        var request = new LoginRequestDto
        {
            UserName = userName,
            Password = password
        };

        var response = await _http.PostAsJsonAsync("api/auth/login", request, ct);
        if (!response.IsSuccessStatusCode)
            return false;

        var dto = await response.Content.ReadFromJsonAsync<LoginResponseDto>(cancellationToken: ct);
        if (dto is null || string.IsNullOrWhiteSpace(dto.Token))
            return false;

        await _tokenStorage.SetTokenAsync(dto.Token);
        _authStateProvider.NotifyUserAuthentication(dto.Token);
        return true;
    }

    public async Task LogoutAsync()
    {
        await _tokenStorage.RemoveTokenAsync();
        _authStateProvider.NotifyUserLogout();
    }
}