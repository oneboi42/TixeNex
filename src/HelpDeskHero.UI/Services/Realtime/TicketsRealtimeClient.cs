using HelpDeskHero.Shared.Contracts.Tickets;
using HelpDeskHero.UI.Services.Auth;
using Microsoft.AspNetCore.SignalR.Client;

namespace HelpDeskHero.UI.Services.Realtime;

public sealed class TicketsRealtimeClient : IAsyncDisposable
{
    private readonly IConfiguration _configuration;
    private readonly TokenStore _tokenStore;
    private HubConnection? _connection;

    public TicketsRealtimeClient(IConfiguration configuration, TokenStore tokenStore)
    {
        _configuration = configuration;
        _tokenStore = tokenStore;
    }

    public event Func<TicketLiveUpdateDto, Task>? OnTicketChanged;

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_connection is not null &&
            (_connection.State == HubConnectionState.Connected ||
             _connection.State == HubConnectionState.Connecting ||
             _connection.State == HubConnectionState.Reconnecting))
        {
            return;
        }

        _connection ??= CreateConnection();

        await _connection.StartAsync(ct);
        await _connection.SendAsync("JoinDashboard", ct);
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

        return connection;
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }
}
