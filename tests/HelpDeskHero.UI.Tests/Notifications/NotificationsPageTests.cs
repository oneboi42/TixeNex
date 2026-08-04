using Bunit;
using FluentAssertions;
using HelpDeskHero.Shared.Contracts.Notifications;
using HelpDeskHero.UI.Pages.Notifications;
using HelpDeskHero.UI.Services.Api;
using HelpDeskHero.UI.Services.Realtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HelpDeskHero.UI.Tests.Notifications;

public sealed class NotificationsPageTests : BunitContext
{
    [Fact]
    public void InitialRefreshFailure_ShowsErrorWithoutPropagating()
    {
        var sessionState = new NotificationSessionState();
        sessionState.BeginIdentity("user-a");
        var notificationApi = new NotificationApiClient(
            new HttpClient(new FailingHandler())
            {
                BaseAddress = new Uri("https://example.test/")
            },
            sessionState);
        var coordinator = new NotificationRealtimeCoordinator(
            new FakeRealtimeClient(),
            notificationApi,
            sessionState,
            NullLogger<NotificationRealtimeCoordinator>.Instance);

        Services.AddSingleton(notificationApi);
        Services.AddSingleton(coordinator);

        var cut = Render(builder =>
        {
            builder.OpenComponent<NotificationsPage>(0);
            builder.CloseComponent();
        });

        cut.Markup.Should().Contain("Could not load notifications.");
        sessionState.UnreadCount.Should().Be(0);
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(
                new HttpRequestException("Notifications unavailable."));
    }

    private sealed class FakeRealtimeClient : INotificationRealtimeClient
    {
        public event Func<UserNotificationDto, Task>? OnNotificationCreated;

        public Task StartAsync(CancellationToken ct = default)
        {
            _ = OnNotificationCreated;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
