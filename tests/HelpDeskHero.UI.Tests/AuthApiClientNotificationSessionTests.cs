using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using FluentAssertions;
using HelpDeskHero.Shared.Contracts.Auth;
using HelpDeskHero.Shared.Contracts.Notifications;
using HelpDeskHero.UI.Services.Api;
using HelpDeskHero.UI.Services.Auth;
using HelpDeskHero.UI.Services.Realtime;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace HelpDeskHero.UI.Tests;

public sealed class AuthApiClientNotificationSessionTests
{
    [Fact]
    public async Task NormalLogout_ResetsUnreadCountStopsRealtimeAndPublishesAnonymousState()
    {
        var sessionState = new NotificationSessionState();
        var realtime = new FakeRealtimeClient();
        var notifications = Enumerable.Range(1, 4)
            .Select(id => new UserNotificationDto
            {
                Id = id,
                Subject = $"Notification {id}",
                Body = $"Notification {id}",
                IsRead = false
            })
            .ToArray();
        await using var coordinator = CreateCoordinator(sessionState, realtime, notifications);
        var js = new MemoryJsRuntime();
        var tokenStore = new TokenStore(js);
        await tokenStore.SetAccessTokenAsync(CreateJwt("user-a"));
        await tokenStore.SetRefreshTokenAsync("refresh-a");
        var authStateProvider = new JwtAuthenticationStateProvider(tokenStore);
        var authClient = new AuthApiClient(
            new SingleClientFactory(new HttpClient(new DelegateHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent))))
            {
                BaseAddress = new Uri("https://example.test/")
            }),
            tokenStore,
            authStateProvider,
            coordinator);

        await coordinator.StartAsync("user-a");
        sessionState.UnreadCount.Should().Be(4);

        await authClient.LogoutAsync();

        sessionState.UnreadCount.Should().Be(0);
        realtime.HasSubscriber.Should().BeFalse();
        realtime.StopCalls.Should().BeGreaterThan(0);
        (await tokenStore.GetAccessTokenAsync()).Should().BeNull();
        (await authStateProvider.GetAuthenticationStateAsync())
            .User.Identity!.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task Login_StartsNewIdentityConnectionAndLoadsItsUnreadCount()
    {
        var sessionState = new NotificationSessionState();
        var realtime = new FakeRealtimeClient();
        var userBNotifications = new[]
        {
            new UserNotificationDto { Id = 1, Subject = "B", Body = "B", IsRead = false }
        };
        await using var coordinator = CreateCoordinator(sessionState, realtime, userBNotifications);
        var tokenStore = new TokenStore(new MemoryJsRuntime());
        var authStateProvider = new JwtAuthenticationStateProvider(tokenStore);
        var loginToken = new TokenResponseDto
        {
            AccessToken = CreateJwt("user-b"),
            RefreshToken = "refresh-b",
            AccessTokenExpiresAtUtc = DateTime.UtcNow.AddMinutes(15),
            RefreshTokenExpiresAtUtc = DateTime.UtcNow.AddDays(1),
            UserName = "user-b",
            DisplayName = "User B"
        };
        var authClient = new AuthApiClient(
            new SingleClientFactory(new HttpClient(new DelegateHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(loginToken)
                })))
            {
                BaseAddress = new Uri("https://example.test/")
            }),
            tokenStore,
            authStateProvider,
            coordinator);

        var loggedIn = await authClient.LoginAsync(new LoginRequestDto
        {
            UserName = "user-b",
            Password = "password"
        });

        loggedIn.Should().BeTrue();
        sessionState.Current.UserId.Should().Be("user-b");
        sessionState.UnreadCount.Should().Be(1);
        realtime.StartCalls.Should().Be(1);
        (await authStateProvider.GetAuthenticationStateAsync())
            .User.FindFirst(ClaimTypes.NameIdentifier)!.Value.Should().Be("user-b");
    }

    private static NotificationRealtimeCoordinator CreateCoordinator(
        NotificationSessionState state,
        FakeRealtimeClient realtime,
        IReadOnlyList<UserNotificationDto> notifications)
    {
        var http = new HttpClient(new DelegateHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(notifications)
            })))
        {
            BaseAddress = new Uri("https://example.test/")
        };

        return new NotificationRealtimeCoordinator(
            realtime,
            new NotificationApiClient(http, state),
            state,
            NullLogger<NotificationRealtimeCoordinator>.Instance);
    }

    private static string CreateJwt(string userId)
    {
        var token = new JwtSecurityToken(
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId),
                new Claim(ClaimTypes.NameIdentifier, userId)
            ],
            expires: DateTime.UtcNow.AddMinutes(15));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class MemoryJsRuntime : IJSRuntime
    {
        private readonly Dictionary<string, string> _storage = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            var key = args?[0]?.ToString() ?? string.Empty;

            if (identifier == "localStorage.setItem")
                _storage[key] = args?[1]?.ToString() ?? string.Empty;
            else if (identifier == "localStorage.removeItem")
                _storage.Remove(key);

            object? result = identifier == "localStorage.getItem" && _storage.TryGetValue(key, out var value)
                ? value
                : default(TValue);

            return ValueTask.FromResult((TValue?)result!);
        }
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public SingleClientFactory(HttpClient client)
        {
            _client = client;
        }

        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class DelegateHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => _handler(request, cancellationToken);
    }

    private sealed class FakeRealtimeClient : INotificationRealtimeClient
    {
        private Func<UserNotificationDto, Task>? _handler;

        public event Func<UserNotificationDto, Task>? OnNotificationCreated
        {
            add => _handler += value;
            remove => _handler -= value;
        }

        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }
        public bool HasSubscriber => _handler is not null;

        public Task StartAsync(CancellationToken ct = default)
        {
            StartCalls++;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            StopCalls++;
            return Task.CompletedTask;
        }
    }
}
