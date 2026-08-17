using FluentAssertions;
using HelpDeskHero.Shared.Contracts.Auth;
using HelpDeskHero.UI.Services.Auth;
using Microsoft.JSInterop;

namespace HelpDeskHero.UI.Tests.Auth;

public sealed class TokenStoreTests
{
    [Fact]
    public async Task NormalSession_UsesLocalStorage()
    {
        var js = new FakeJsRuntime();
        var store = new TokenStore(js);

        await store.SetAuthenticationAsync(
            new TokenResponseDto
            {
                AccessToken = "normal-access",
                RefreshToken = "normal-refresh",
                IsDemoUser = false
            });

        js.Local["auth.access_token"]
            .Should().Be("normal-access");

        js.Session.Should()
            .NotContainKey("auth.access_token");
    }

    [Fact]
    public async Task DemoSession_UsesSessionStorage()
    {
        var js = new FakeJsRuntime();
        var store = new TokenStore(js);

        await store.SetAuthenticationAsync(
            DemoToken("demo-access"));

        js.Session["auth.access_token"]
            .Should().Be("demo-access");

        js.Local.Should()
            .NotContainKey("auth.access_token");
    }

    [Fact]
    public async Task DemoTabs_HaveIndependentSessions()
    {
        var userStore =
            new TokenStore(new FakeJsRuntime());

        var agentStore =
            new TokenStore(new FakeJsRuntime());

        var adminStore =
            new TokenStore(new FakeJsRuntime());

        await userStore.SetAuthenticationAsync(
            DemoToken("user-token"));

        await agentStore.SetAuthenticationAsync(
            DemoToken("agent-token"));

        await adminStore.SetAuthenticationAsync(
            DemoToken("admin-token"));

        (await userStore.GetAccessTokenAsync())
            .Should().Be("user-token");

        (await agentStore.GetAccessTokenAsync())
            .Should().Be("agent-token");

        (await adminStore.GetAccessTokenAsync())
            .Should().Be("admin-token");
    }

    private static TokenResponseDto DemoToken(
        string accessToken)
    {
        return new TokenResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = $"refresh-{accessToken}",
            IsDemoUser = true,
            DemoExpiresAtUtc =
                DateTime.UtcNow.AddHours(1),
            DemoAbsoluteExpiresAtUtc =
                DateTime.UtcNow.AddHours(3)
        };
    }

    private sealed class FakeJsRuntime : IJSRuntime
    {
        public Dictionary<string, string> Local { get; } = [];
        public Dictionary<string, string> Session { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            object?[]? args)
        {
            return InvokeAsync<TValue>(
                identifier,
                CancellationToken.None,
                args);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            var storage =
                identifier.StartsWith("sessionStorage")
                    ? Session
                    : Local;

            var key =
                args![0]!.ToString()!;

            if (identifier.EndsWith("setItem"))
            {
                storage[key] =
                    args[1]!.ToString()!;
            }
            else if (identifier.EndsWith("removeItem"))
            {
                storage.Remove(key);
            }

            object? result =
                identifier.EndsWith("getItem") &&
                storage.TryGetValue(key, out var value)
                    ? value
                    : default(TValue);

            return ValueTask.FromResult(
                (TValue?)result!);
        }
    }
}