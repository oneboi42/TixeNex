using Blazored.LocalStorage;
using HelpDeskHero.Shared.Contracts.Auth;

namespace HelpDeskHero.UI.Services.Auth;

public sealed class SessionTokenStore
{
    private const string Key = "helpdeskhero.auth";
    private readonly ILocalStorageService _localStorage;

    public SessionTokenStore(ILocalStorageService localStorage)
    {
        _localStorage = localStorage;
    }

    public ValueTask SetAsync(AuthResponseDto auth, CancellationToken ct = default) =>
        _localStorage.SetItemAsync(Key, auth, ct);

    public ValueTask<AuthResponseDto?> GetAsync(CancellationToken ct = default) =>
        _localStorage.GetItemAsync<AuthResponseDto>(Key, ct);

    public ValueTask RemoveAsync(CancellationToken ct = default) =>
        _localStorage.RemoveItemAsync(Key, ct);
}