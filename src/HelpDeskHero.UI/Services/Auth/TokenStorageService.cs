using Microsoft.JSInterop;

namespace HelpDeskHero.UI.Services.Auth;

public sealed class TokenStorageService
{
    private readonly IJSRuntime _js;

    public TokenStorageService(IJSRuntime js)
    {
        _js = js;
    }

    public ValueTask<string?> GetTokenAsync()
        => _js.InvokeAsync<string?>("authStorage.getToken");

    public ValueTask SetTokenAsync(string token)
        => _js.InvokeVoidAsync("authStorage.setToken", token);

    public ValueTask RemoveTokenAsync()
        => _js.InvokeVoidAsync("authStorage.removeToken");
}