using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HelpDeskHero.Shared.Contracts.Notifications;
using HelpDeskHero.UI.Services.Api;
using HelpDeskHero.UI.Services.Realtime;
using Microsoft.Extensions.Logging.Abstractions;

namespace HelpDeskHero.UI.Tests;

public sealed class NotificationRealtimeCoordinatorTests
{
    [Fact]
    public async Task Reset_ClearsCountStopsConnectionAndAllowsAnotherIdentityToStart()
    {
        var responses = new Queue<IReadOnlyList<UserNotificationDto>>();
        responses.Enqueue(CreateNotifications(2));
        responses.Enqueue(CreateNotifications(1));
        var state = new NotificationSessionState();
        var realtime = new FakeNotificationRealtimeClient();
        await using var coordinator = CreateCoordinator(state, realtime, _ => responses.Dequeue());
        var counts = new List<int>();
        state.UnreadCountChanged += counts.Add;

        await coordinator.StartAsync("user-a");
        state.UnreadCount.Should().Be(2);

        await coordinator.ResetAsync();
        state.UnreadCount.Should().Be(0);
        realtime.HasSubscriber.Should().BeFalse();

        await coordinator.StartAsync("user-b");
        state.UnreadCount.Should().Be(1);
        realtime.StartCalls.Should().Be(2);
        realtime.StopCalls.Should().BeGreaterThan(0);
        realtime.HasSubscriber.Should().BeTrue();
        counts.Should().ContainInOrder(2, 0, 0, 1);
    }

    [Fact]
    public async Task StaleRestResponse_CannotOverwriteNewIdentityCount()
    {
        var state = new NotificationSessionState();
        var pendingResponse = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new NotificationApiClient(
            new HttpClient(new DelegateHandler((_, _) => pendingResponse.Task))
            {
                BaseAddress = new Uri("https://example.test/")
            },
            state);

        state.BeginIdentity("user-a");
        var staleRequest = api.GetMineAndUpdateUnreadCountAsync();

        var userBSession = state.BeginIdentity("user-b");
        state.TryUpdateUnreadCount(userBSession, 1).Should().BeTrue();

        pendingResponse.SetResult(JsonResponse(CreateNotifications(5)));
        await staleRequest;

        state.UnreadCount.Should().Be(1);
    }

    [Fact]
    public async Task OldConnectionEvent_IsRejectedAfterIdentitySwitch()
    {
        var state = new NotificationSessionState();
        var realtime = new FakeNotificationRealtimeClient();
        await using var coordinator = CreateCoordinator(state, realtime, _ => []);
        var received = new List<string>();
        coordinator.NotificationReceived += notification =>
        {
            received.Add(notification.Subject);
            return Task.CompletedTask;
        };

        await coordinator.StartAsync("user-a");
        var userAHandler = realtime.CurrentHandler!;

        await coordinator.ResetAsync();
        await coordinator.StartAsync("user-b");

        await userAHandler(Notification(10, "User A notification"));
        received.Should().BeEmpty();

        await realtime.RaiseAsync(Notification(11, "User B notification"));
        received.Should().Equal("User B notification");
    }

    [Fact]
    public async Task ReceivedIds_AreClearedBetweenIdentities()
    {
        var state = new NotificationSessionState();
        var realtime = new FakeNotificationRealtimeClient();
        await using var coordinator = CreateCoordinator(state, realtime, _ => []);
        var received = new List<string>();
        coordinator.NotificationReceived += notification =>
        {
            received.Add(notification.Subject);
            return Task.CompletedTask;
        };

        await coordinator.StartAsync("user-a");
        await realtime.RaiseAsync(Notification(7, "A"));

        await coordinator.ResetAsync();
        await coordinator.StartAsync("user-b");
        await realtime.RaiseAsync(Notification(7, "B"));

        received.Should().Equal("A", "B");
    }

    [Fact]
    public async Task Dispose_RemovesRealtimeAndSessionHandlers()
    {
        var state = new NotificationSessionState();
        var realtime = new FakeNotificationRealtimeClient();
        var coordinator = CreateCoordinator(state, realtime, _ => []);

        await coordinator.StartAsync("user-a");
        await coordinator.DisposeAsync();
        var stopCallsAfterDispose = realtime.StopCalls;

        realtime.HasSubscriber.Should().BeFalse();
        await state.ResetAsync();
        realtime.StopCalls.Should().Be(stopCallsAfterDispose);
    }

    [Fact]
    public void MarkingNotificationsRead_StillUpdatesUnreadCount()
    {
        var state = new NotificationSessionState();
        state.BeginIdentity("user-a");
        var api = new NotificationApiClient(
            new HttpClient(new DelegateHandler((_, _) =>
                Task.FromResult(JsonResponse(Array.Empty<UserNotificationDto>()))))
            {
                BaseAddress = new Uri("https://example.test/")
            },
            state);

        api.UpdateUnreadCount(CreateNotifications(3));
        state.UnreadCount.Should().Be(3);

        var notifications = CreateNotifications(3).ToList();
        notifications[0].IsRead = true;
        api.UpdateUnreadCount(notifications);

        state.UnreadCount.Should().Be(2);
    }

    private static NotificationRealtimeCoordinator CreateCoordinator(
        NotificationSessionState state,
        FakeNotificationRealtimeClient realtime,
        Func<HttpRequestMessage, IReadOnlyList<UserNotificationDto>> getNotifications)
    {
        var http = new HttpClient(new DelegateHandler((request, _) =>
            Task.FromResult(JsonResponse(getNotifications(request)))))
        {
            BaseAddress = new Uri("https://example.test/")
        };

        return new NotificationRealtimeCoordinator(
            realtime,
            new NotificationApiClient(http, state),
            state,
            NullLogger<NotificationRealtimeCoordinator>.Instance);
    }

    private static IReadOnlyList<UserNotificationDto> CreateNotifications(int unreadCount) =>
        Enumerable.Range(1, unreadCount)
            .Select(id => Notification(id, $"Notification {id}"))
            .ToArray();

    private static UserNotificationDto Notification(int id, string subject) => new()
    {
        Id = id,
        Subject = subject,
        Body = subject,
        IsRead = false,
        CreatedAtUtc = DateTime.UtcNow
    };

    private static HttpResponseMessage JsonResponse<T>(T value) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

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

    private sealed class FakeNotificationRealtimeClient : INotificationRealtimeClient
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
        public Func<UserNotificationDto, Task>? CurrentHandler => _handler;

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
