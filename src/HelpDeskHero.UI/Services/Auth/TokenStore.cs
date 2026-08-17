using System.Globalization;
using HelpDeskHero.Shared.Contracts.Auth;
using Microsoft.JSInterop;

namespace HelpDeskHero.UI.Services.Auth;

public sealed class TokenStore
{
    private const string AccessTokenKey = "auth.access_token";
    private const string RefreshTokenKey = "auth.refresh_token";
    private const string SessionTypeKey = "auth.session_type";
    private const string DemoExpiresKey = "auth.demo_expires";
    private const string DemoAbsoluteExpiresKey = "auth.demo_absolute_expires";

    private readonly IJSRuntime _js;

    public TokenStore(IJSRuntime js)
    {
        _js = js;
    }

    public event Action? SessionChanged;

    public async Task SetAuthenticationAsync(TokenResponseDto dto)
    {
        if (dto.IsDemoUser)
        {
            await SetSessionTypeAsync("demo");

            await SetAsync(
                "sessionStorage",
                AccessTokenKey,
                dto.AccessToken);

            await SetAsync(
                "sessionStorage",
                RefreshTokenKey,
                dto.RefreshToken);

            await SetDateAsync(
                DemoExpiresKey,
                dto.DemoExpiresAtUtc);

            await SetDateAsync(
                DemoAbsoluteExpiresKey,
                dto.DemoAbsoluteExpiresAtUtc);
        }
        else
        {
            await ClearDemoDataAsync();
            await SetSessionTypeAsync("normal");

            await SetAsync(
                "localStorage",
                AccessTokenKey,
                dto.AccessToken);

            await SetAsync(
                "localStorage",
                RefreshTokenKey,
                dto.RefreshToken);
        }

        SessionChanged?.Invoke();
    }

    public async Task<string?> GetAccessTokenAsync()
    {
        var storage = await GetReadStorageAsync();

        return storage is null
            ? null
            : await GetAsync(storage, AccessTokenKey);
    }

    public async Task<string?> GetRefreshTokenAsync()
    {
        var storage = await GetReadStorageAsync();

        return storage is null
            ? null
            : await GetAsync(storage, RefreshTokenKey);
    }

    public async Task SetAccessTokenAsync(string token)
    {
        var storage = await GetWriteStorageAsync();
        await SetAsync(storage, AccessTokenKey, token);
    }

    public async Task SetRefreshTokenAsync(string token)
    {
        var storage = await GetWriteStorageAsync();
        await SetAsync(storage, RefreshTokenKey, token);
    }

    public async Task<bool> GetIsDemoUserAsync()
    {
        return await GetSessionTypeAsync() == "demo";
    }

    public Task<DateTime?> GetDemoExpiresAtUtcAsync()
    {
        return GetDateAsync(DemoExpiresKey);
    }

    public Task<DateTime?> GetDemoAbsoluteExpiresAtUtcAsync()
    {
        return GetDateAsync(DemoAbsoluteExpiresKey);
    }

    public async Task ClearAsync()
    {
        if (await GetIsDemoUserAsync())
        {
            await ClearDemoDataAsync();
        }
        else
        {
            await RemoveAsync(
                "localStorage",
                AccessTokenKey);

            await RemoveAsync(
                "localStorage",
                RefreshTokenKey);
        }

        await SetSessionTypeAsync("none");

        SessionChanged?.Invoke();
    }

    private async Task<string?> GetReadStorageAsync()
    {
        return await GetSessionTypeAsync() switch
        {
            "demo" => "sessionStorage",
            "none" => null,
            _ => "localStorage"
        };
    }

    private async Task<string> GetWriteStorageAsync()
    {
        var sessionType = await GetSessionTypeAsync();

        if (sessionType == "demo")
            return "sessionStorage";

        if (sessionType == "none")
            await SetSessionTypeAsync("normal");

        return "localStorage";
    }

    private Task<string?> GetSessionTypeAsync()
    {
        return GetAsync(
            "sessionStorage",
            SessionTypeKey);
    }

    private Task SetSessionTypeAsync(string type)
    {
        return SetAsync(
            "sessionStorage",
            SessionTypeKey,
            type);
    }

    private async Task ClearDemoDataAsync()
    {
        await RemoveAsync("sessionStorage", AccessTokenKey);
        await RemoveAsync("sessionStorage", RefreshTokenKey);
        await RemoveAsync("sessionStorage", DemoExpiresKey);
        await RemoveAsync("sessionStorage", DemoAbsoluteExpiresKey);
    }
    private static DateTime NormalizeUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }
    private async Task SetDateAsync(
        string key,
        DateTime? value)
    {
        if (value is null)
        {
            await RemoveAsync("sessionStorage", key);
            return;
        }

        await SetAsync(
            "sessionStorage",
            key,
            NormalizeUtc(value.Value)
                .ToString("o", CultureInfo.InvariantCulture));
    }

    private async Task<DateTime?> GetDateAsync(string key)
    {
        var value = await GetAsync(
            "sessionStorage",
            key);

        if (string.IsNullOrWhiteSpace(value))
            return null;

        return DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var result)
                ? NormalizeUtc(result)
                : null;
    }

    private Task SetAsync(
        string storage,
        string key,
        string value)
    {
        return _js.InvokeVoidAsync(
            $"{storage}.setItem",
            key,
            value).AsTask();
    }

    private async Task<string?> GetAsync(
        string storage,
        string key)
    {
        return await _js.InvokeAsync<string?>(
            $"{storage}.getItem",
            key);
    }

    private Task RemoveAsync(
        string storage,
        string key)
    {
        return _js.InvokeVoidAsync(
            $"{storage}.removeItem",
            key).AsTask();
    }
}