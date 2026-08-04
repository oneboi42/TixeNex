using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Bunit;
using FluentAssertions;
using HelpDeskHero.Shared.Contracts.Notifications;
using HelpDeskHero.UI.Layout;
using HelpDeskHero.UI.Services.Api;
using HelpDeskHero.UI.Services.Realtime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HelpDeskHero.UI.Tests;

public sealed class MainLayoutNotificationStartupTests : BunitContext
{
    [Fact]
    public async Task PersistedLogin_NotificationStartupFailureIsNonFatalAndAllowsRetry()
    {
        const string userId = "persisted-user";
        Services.AddAuthorizationCore();

        var authenticationStateProvider = new PersistedAuthenticationStateProvider(userId);
        var sessionState = new NotificationSessionState();
        var realtime = new FailOnceRealtimeClient();
        var notificationApi = new NotificationApiClient(
            new HttpClient(new DelegateHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(Array.Empty<UserNotificationDto>())
                })))
            {
                BaseAddress = new Uri("https://example.test/")
            },
            sessionState);
        var logger = new RecordingLogger<NotificationRealtimeCoordinator>();
        var coordinator = new NotificationRealtimeCoordinator(
            realtime,
            notificationApi,
            sessionState,
            logger);

        Services.AddSingleton<AuthenticationStateProvider>(authenticationStateProvider);
        Services.AddSingleton<IAuthorizationService, PermissiveAuthorizationService>();
        Services.AddSingleton(notificationApi);
        Services.AddSingleton(coordinator);

        var cut = Render(builder =>
        {
            builder.OpenComponent<CascadingAuthenticationState>(0);
            builder.AddAttribute(1, "ChildContent", (RenderFragment)(childBuilder =>
            {
                childBuilder.OpenComponent<MainLayout>(2);
                childBuilder.AddAttribute(3, "Body", (RenderFragment)(bodyBuilder =>
                    bodyBuilder.AddMarkupContent(4, "<p>Persisted session content</p>")));
                childBuilder.CloseComponent();
            }));
            builder.CloseComponent();
        });

        cut.Markup.Should().Contain("Persisted session content");
        (await authenticationStateProvider.GetAuthenticationStateAsync())
            .User.Identity!.IsAuthenticated.Should().BeTrue();
        sessionState.UnreadCount.Should().Be(0);
        realtime.StartCalls.Should().Be(1);
        realtime.HasSubscriber.Should().BeFalse();
        logger.Entries.Should().ContainSingle(entry =>
            entry.Level == LogLevel.Warning &&
            entry.Exception is HttpRequestException &&
            entry.Message.Contains("authenticated session will continue"));

        await coordinator.StartAsync(userId);

        realtime.StartCalls.Should().Be(2);
        realtime.HasSubscriber.Should().BeTrue();
        sessionState.UnreadCount.Should().Be(0);
    }

    private sealed class PersistedAuthenticationStateProvider : AuthenticationStateProvider
    {
        private readonly AuthenticationState _state;

        public PersistedAuthenticationStateProvider(string userId)
        {
            var identity = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId),
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Name, userId)
            ],
            "persisted-token");
            _state = new AuthenticationState(new ClaimsPrincipal(identity));
        }

        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(_state);
    }

    private sealed class PermissiveAuthorizationService : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            IEnumerable<IAuthorizationRequirement> requirements) =>
            Task.FromResult(AuthorizationResult.Success());

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            string policyName) =>
            Task.FromResult(AuthorizationResult.Success());
    }

    private sealed class FailOnceRealtimeClient : INotificationRealtimeClient
    {
        private Func<UserNotificationDto, Task>? _handler;

        public event Func<UserNotificationDto, Task>? OnNotificationCreated
        {
            add => _handler += value;
            remove => _handler -= value;
        }

        public int StartCalls { get; private set; }
        public bool HasSubscriber => _handler is not null;

        public Task StartAsync(CancellationToken ct = default)
        {
            StartCalls++;
            return StartCalls == 1
                ? Task.FromException(new HttpRequestException("SignalR negotiate failed."))
                : Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
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

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);
}
