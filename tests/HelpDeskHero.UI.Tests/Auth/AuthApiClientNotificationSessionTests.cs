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

namespace HelpDeskHero.UI.Tests.Auth;

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
        (await tokenStore.GetAccessTokenAsync()).Should().Be(loginToken.AccessToken);
        (await tokenStore.GetRefreshTokenAsync()).Should().Be(loginToken.RefreshToken);
        sessionState.Current.UserId.Should().Be("user-b");
        sessionState.UnreadCount.Should().Be(1);
        realtime.StartCalls.Should().Be(1);
        realtime.ActiveConnections.Should().Be(1);
        realtime.SubscriberCount.Should().Be(1);
        (await authStateProvider.GetAuthenticationStateAsync())
            .User.FindFirst(ClaimTypes.NameIdentifier)!.Value.Should().Be("user-b");
    }

    [Fact]
    public async Task Login_InvalidCredentialsDoesNotAuthenticateOrStartNotifications()
    {
        var sessionState = new NotificationSessionState();
        var realtime = new FakeRealtimeClient();
        await using var coordinator = CreateCoordinator(sessionState, realtime, []);
        var tokenStore = new TokenStore(new MemoryJsRuntime());
        var authStateProvider = new JwtAuthenticationStateProvider(tokenStore);
        var authClient = new AuthApiClient(
            new SingleClientFactory(new HttpClient(new DelegateHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized))))
            {
                BaseAddress = new Uri("https://example.test/")
            }),
            tokenStore,
            authStateProvider,
            coordinator);

        var loggedIn = await authClient.LoginAsync(new LoginRequestDto
        {
            UserName = "user-b",
            Password = "wrong-password"
        });

        loggedIn.Should().BeFalse();
        realtime.StartCalls.Should().Be(0);
        realtime.SubscriberCount.Should().Be(0);
        (await tokenStore.GetAccessTokenAsync()).Should().BeNull();
        (await authStateProvider.GetAuthenticationStateAsync())
            .User.Identity!.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task Login_SignalRStartupFailureKeepsAuthenticationAndAllowsCleanRetry()
    {
        var sessionState = new NotificationSessionState();
        var realtime = new FakeRealtimeClient();
        var startFailure = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        realtime.EnqueueStart(startFailure.Task);
        await using var coordinator = CreateCoordinator(sessionState, realtime, []);
        var tokenStore = new TokenStore(new MemoryJsRuntime());
        var authStateProvider = new JwtAuthenticationStateProvider(tokenStore);
        var loginToken = CreateTokenResponse("user-b");
        var authClient = CreateAuthClient(
            loginToken,
            tokenStore,
            authStateProvider,
            coordinator);

        var loginTask = authClient.LoginAsync(new LoginRequestDto
        {
            UserName = "user-b",
            Password = "password"
        });
        realtime.StartCalls.Should().Be(1);

        startFailure.SetException(new InvalidOperationException("SignalR unavailable."));

        (await loginTask).Should().BeTrue();
        (await tokenStore.GetAccessTokenAsync()).Should().Be(loginToken.AccessToken);
        (await tokenStore.GetRefreshTokenAsync()).Should().Be(loginToken.RefreshToken);
        (await authStateProvider.GetAuthenticationStateAsync())
            .User.Identity!.IsAuthenticated.Should().BeTrue();
        sessionState.UnreadCount.Should().Be(0);
        realtime.ActiveConnections.Should().Be(0);
        realtime.SubscriberCount.Should().Be(0);

        var received = 0;
        coordinator.NotificationReceived += _ =>
        {
            received++;
            return Task.CompletedTask;
        };

        await coordinator.StartAsync("user-b");
        await realtime.RaiseAsync(new UserNotificationDto
        {
            Id = 42,
            Subject = "Retry",
            Body = "Retry"
        });

        realtime.StartCalls.Should().Be(2);
        realtime.ActiveConnections.Should().Be(1);
        realtime.MaxActiveConnections.Should().Be(1);
        realtime.SubscriberCount.Should().Be(1);
        received.Should().Be(1);
    }

    [Fact]
    public async Task Login_InitialNotificationRefreshFailureKeepsAuthenticationAndAllowsCleanRetry()
    {
        var sessionState = new NotificationSessionState();
        var realtime = new FakeRealtimeClient();
        var refreshFailure = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var notificationCalls = 0;
        await using var coordinator = CreateCoordinator(
            sessionState,
            realtime,
            (_, _) =>
            {
                notificationCalls++;
                return notificationCalls == 1
                    ? refreshFailure.Task
                    : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = JsonContent.Create(Array.Empty<UserNotificationDto>())
                    });
            });
        var tokenStore = new TokenStore(new MemoryJsRuntime());
        var authStateProvider = new JwtAuthenticationStateProvider(tokenStore);
        var loginToken = CreateTokenResponse("user-b");
        var authClient = CreateAuthClient(
            loginToken,
            tokenStore,
            authStateProvider,
            coordinator);

        var loginTask = authClient.LoginAsync(new LoginRequestDto
        {
            UserName = "user-b",
            Password = "password"
        });
        notificationCalls.Should().Be(1);

        refreshFailure.SetException(new HttpRequestException("Notifications unavailable."));

        (await loginTask).Should().BeTrue();
        (await tokenStore.GetAccessTokenAsync()).Should().Be(loginToken.AccessToken);
        (await authStateProvider.GetAuthenticationStateAsync())
            .User.Identity!.IsAuthenticated.Should().BeTrue();
        sessionState.UnreadCount.Should().Be(0);
        realtime.ActiveConnections.Should().Be(0);
        realtime.SubscriberCount.Should().Be(0);

        await coordinator.StartAsync("user-b");

        notificationCalls.Should().Be(2);
        realtime.StartCalls.Should().Be(2);
        realtime.ActiveConnections.Should().Be(1);
        realtime.MaxActiveConnections.Should().Be(1);
        realtime.SubscriberCount.Should().Be(1);
    }

    private static NotificationRealtimeCoordinator CreateCoordinator(
        NotificationSessionState state,
        FakeRealtimeClient realtime,
        IReadOnlyList<UserNotificationDto> notifications)
    {
        return CreateCoordinator(
            state,
            realtime,
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(notifications)
            }));
    }

    private static NotificationRealtimeCoordinator CreateCoordinator(
        NotificationSessionState state,
        FakeRealtimeClient realtime,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        var http = new HttpClient(new DelegateHandler(handler))
        {
            BaseAddress = new Uri("https://example.test/")
        };

        return new NotificationRealtimeCoordinator(
            realtime,
            new NotificationApiClient(http, state),
            state,
            NullLogger<NotificationRealtimeCoordinator>.Instance);
    }

    private static AuthApiClient CreateAuthClient(
        TokenResponseDto token,
        TokenStore tokenStore,
        JwtAuthenticationStateProvider authStateProvider,
        NotificationRealtimeCoordinator coordinator)
    {
        return new AuthApiClient(
            new SingleClientFactory(new HttpClient(new DelegateHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(token)
                })))
            {
                BaseAddress = new Uri("https://example.test/")
            }),
            tokenStore,
            authStateProvider,
            coordinator);
    }

    private static TokenResponseDto CreateTokenResponse(string userId) => new()
    {
        AccessToken = CreateJwt(userId),
        RefreshToken = $"refresh-{userId}",
        AccessTokenExpiresAtUtc = DateTime.UtcNow.AddMinutes(15),
        RefreshTokenExpiresAtUtc = DateTime.UtcNow.AddDays(1),
        UserName = userId,
        DisplayName = userId
    };

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
        private readonly Queue<Task> _startTasks = new();

        public event Func<UserNotificationDto, Task>? OnNotificationCreated
        {
            add => _handler += value;
            remove => _handler -= value;
        }

        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }
        public int ActiveConnections { get; private set; }
        public int MaxActiveConnections { get; private set; }
        public bool HasSubscriber => _handler is not null;
        public int SubscriberCount => _handler?.GetInvocationList().Length ?? 0;

        public void EnqueueStart(Task task) => _startTasks.Enqueue(task);

        public async Task StartAsync(CancellationToken ct = default)
        {
            StartCalls++;
            if (_startTasks.Count > 0)
                await _startTasks.Dequeue().WaitAsync(ct);

            ActiveConnections++;
            MaxActiveConnections = Math.Max(MaxActiveConnections, ActiveConnections);
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            StopCalls++;
            ActiveConnections = Math.Max(0, ActiveConnections - 1);
            return Task.CompletedTask;
        }

        public async Task RaiseAsync(UserNotificationDto notification)
        {
            var handlers = _handler;
            if (handlers is null)
                return;

            foreach (var handler in handlers.GetInvocationList().Cast<Func<UserNotificationDto, Task>>())
                await handler(notification);
        }
    }
}
