using HelpDeskHero.Shared.Contracts.Tickets;
using HelpDeskHero.Shared.Contracts.Notifications;
using HelpDeskHero.UI.Services.Auth;
using Microsoft.AspNetCore.SignalR.Client;

namespace HelpDeskHero.UI.Services.Realtime;

public sealed class TicketsRealtimeClient : ITicketsRealtimeClient, INotificationRealtimeClient, IAsyncDisposable
{
    private readonly IConfiguration _configuration;
    private readonly TokenStore _tokenStore;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private HubConnection? _connection;

    public TicketsRealtimeClient(IConfiguration configuration, TokenStore tokenStore)
    {
        _configuration = configuration;
        _tokenStore = tokenStore;
    }

    public event Func<TicketLiveUpdateDto, Task>? OnTicketChanged;
    public event Func<UserNotificationDto, Task>? OnNotificationCreated;

    public async Task StartAsync(CancellationToken ct = default)
    {
        await _connectionLock.WaitAsync(ct);
        try
        {
            if (_connection is not null &&
                (_connection.State == HubConnectionState.Connected ||
                 _connection.State == HubConnectionState.Connecting ||
                 _connection.State == HubConnectionState.Reconnecting))
            {
                return;
            }

            _connection ??= CreateConnection();

            try
            {
                await _connection.StartAsync(ct);
                await _connection.SendAsync("JoinDashboard", ct);
            }
            catch
            {
                await _connection.DisposeAsync();
                _connection = null;
                throw;
            }
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        await _connectionLock.WaitAsync(ct);
        try
        {
            if (_connection is null)
                return;

            var connection = _connection;
            _connection = null;

            try
            {
                await connection.StopAsync(ct);
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private HubConnection CreateConnection()
    {
        var apiBaseUrl = _configuration["Api:BaseUrl"]
            ?? throw new InvalidOperationException("Missing Api:BaseUrl.");

        var hubUrl = new Uri(new Uri(apiBaseUrl.TrimEnd('/') + "/"), "hubs/tickets");

        var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.AccessTokenProvider = async () =>
                    await _tokenStore.GetAccessTokenAsync();
            })
            .WithAutomaticReconnect()
            .Build();

        connection.On<TicketLiveUpdateDto>("TicketChanged", async update =>
        {
            if (OnTicketChanged is not null)
            {
                await OnTicketChanged(update);
            }
        });

        connection.On<UserNotificationDto>("NotificationCreated", async notification =>
        {
            if (OnNotificationCreated is not null)
            {
                await OnNotificationCreated(notification);
            }
        });

        return connection;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _connectionLock.Dispose();
    }
}
