using System.Net.Http.Json;
using System.Threading.Channels;
using FluentAssertions;
using HelpDeskHero.Api.Application.Interfaces;
using HelpDeskHero.Api.Domain;
using HelpDeskHero.Api.Infrastructure.Persistence;
using HelpDeskHero.Api.Infrastructure.Services;
using HelpDeskHero.Api.IntegrationTests.Infrastructure;
using HelpDeskHero.Shared.Contracts.Auth;
using HelpDeskHero.Shared.Contracts.Tickets;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDeskHero.Api.IntegrationTests.Realtime;

[Collection("ApiIntegration")]
public sealed class TicketsHubAuthorizationTests
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public TicketsHubAuthorizationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task JoinTicket_AsUser_CannotJoinAnotherUsersTicket()
    {
        var otherUserId = await GetUserIdAsync("agent1");
        var ticket = await SeedTicketAsync(
            $"signalr-user-denied-{Guid.NewGuid():N}",
            requesterUserId: otherUserId);
        var token = await LoginAsync("user", CustomWebApplicationFactory.UserPassword);

        await using var connection = await ConnectAsync(token);

        await Assert.ThrowsAsync<HubException>(() =>
            connection.InvokeAsync("JoinTicket", ticket.Id.ToString()));
    }

    [Fact]
    public async Task JoinTicket_AsAgent_CannotJoinUnrelatedTicket()
    {
        var otherAgentId = await GetUserIdAsync("agent1");
        var otherUserId = await GetUserIdAsync("user");
        var ticket = await SeedTicketAsync(
            $"signalr-agent-denied-{Guid.NewGuid():N}",
            requesterUserId: otherUserId,
            assignedToUserId: otherAgentId);
        var token = await LoginAsync("agent", CustomWebApplicationFactory.AgentPassword);

        await using var connection = await ConnectAsync(token);

        await Assert.ThrowsAsync<HubException>(() =>
            connection.InvokeAsync("JoinTicket", ticket.Id.ToString()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task JoinTicket_AsAdmin_CannotJoinOppositeWorkspaceWithoutLeakingExistence(
        bool adminIsDemoWorkspace)
    {
        var ticket = await SeedTicketAsync(
            $"signalr-admin-cross-workspace-{Guid.NewGuid():N}",
            demoExpiresAtUtc: adminIsDemoWorkspace
                ? null
                : DateTime.UtcNow.AddHours(1));
        var token = adminIsDemoWorkspace
            ? await GetDemoAdminTokenAsync()
            : await LoginAsync("admin", CustomWebApplicationFactory.AdminPassword);

        await using var connection = await ConnectAsync(token);

        var crossWorkspaceError = await Assert.ThrowsAsync<HubException>(() =>
            connection.InvokeAsync("JoinTicket", ticket.Id.ToString()));
        var missingTicketError = await Assert.ThrowsAsync<HubException>(() =>
            connection.InvokeAsync("JoinTicket", int.MaxValue.ToString()));

        crossWorkspaceError.Message.Should().Be(missingTicketError.Message);
    }

    [Fact]
    public async Task JoinTicket_AuthorizedUserAgentAndAdmins_ReceiveTicketUpdates()
    {
        var userId = await GetUserIdAsync("user");
        var agentId = await GetUserIdAsync("agent");
        var userTicket = await SeedTicketAsync(
            $"signalr-user-allowed-{Guid.NewGuid():N}",
            requesterUserId: userId);
        var agentTicket = await SeedTicketAsync(
            $"signalr-agent-allowed-{Guid.NewGuid():N}",
            assignedToUserId: agentId);
        var normalAdminTicket = await SeedTicketAsync(
            $"signalr-admin-allowed-{Guid.NewGuid():N}");
        var demoAdminTicket = await SeedTicketAsync(
            $"signalr-demo-admin-allowed-{Guid.NewGuid():N}",
            demoExpiresAtUtc: DateTime.UtcNow.AddHours(1));

        await AssertCanJoinAndReceiveAsync(
            await LoginAsync("user", CustomWebApplicationFactory.UserPassword),
            userTicket);
        await AssertCanJoinAndReceiveAsync(
            await LoginAsync("agent", CustomWebApplicationFactory.AgentPassword),
            agentTicket);
        await AssertCanJoinAndReceiveAsync(
            await LoginAsync("admin", CustomWebApplicationFactory.AdminPassword),
            normalAdminTicket);
        await AssertCanJoinAndReceiveAsync(
            await GetDemoAdminTokenAsync(),
            demoAdminTicket);
    }

    [Fact]
    public async Task TicketUpdates_StopWhenAgentLosesRestVisibilityAfterJoining()
    {
        var agentId = await GetUserIdAsync("agent");
        var otherAgentId = await GetUserIdAsync("agent1");
        var ticket = await SeedTicketAsync(
            $"signalr-agent-reassigned-{Guid.NewGuid():N}",
            assignedToUserId: agentId);
        var updates = Channel.CreateUnbounded<TicketLiveUpdateDto>();

        await using var connection = await ConnectAsync(
            await LoginAsync("agent", CustomWebApplicationFactory.AgentPassword),
            updates.Writer);

        await connection.InvokeAsync("JoinTicket", ticket.Id.ToString());
        await NotifyAsync(ticket);
        await ReadTicketAsync(updates.Reader, ticket.Id);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var storedTicket = await db.Tickets.SingleAsync(item => item.Id == ticket.Id);
            storedTicket.AssignedToUserId = otherAgentId;
            await db.SaveChangesAsync();
        }

        await NotifyAsync(ticket);
        await AssertNoTicketAsync(updates.Reader, ticket.Id);
    }

    [Fact]
    public async Task DashboardUpdates_MatchRestTicketVisibility()
    {
        var userId = await GetUserIdAsync("user");
        var agentId = await GetUserIdAsync("agent");
        var otherUserId = await GetUserIdAsync("agent1");
        var userTicket = await SeedTicketAsync(
            $"signalr-dashboard-user-{Guid.NewGuid():N}",
            requesterUserId: userId);
        var agentTicket = await SeedTicketAsync(
            $"signalr-dashboard-agent-{Guid.NewGuid():N}",
            requesterUserId: otherUserId,
            assignedToUserId: agentId);
        var unrelatedTicket = await SeedTicketAsync(
            $"signalr-dashboard-unrelated-{Guid.NewGuid():N}",
            requesterUserId: otherUserId);

        var userUpdates = Channel.CreateUnbounded<TicketLiveUpdateDto>();
        var agentUpdates = Channel.CreateUnbounded<TicketLiveUpdateDto>();
        var adminUpdates = Channel.CreateUnbounded<TicketLiveUpdateDto>();

        await using var userConnection = await ConnectAsync(
            await LoginAsync("user", CustomWebApplicationFactory.UserPassword),
            userUpdates.Writer);
        await using var agentConnection = await ConnectAsync(
            await LoginAsync("agent", CustomWebApplicationFactory.AgentPassword),
            agentUpdates.Writer);
        await using var adminConnection = await ConnectAsync(
            await LoginAsync("admin", CustomWebApplicationFactory.AdminPassword),
            adminUpdates.Writer);

        await userConnection.InvokeAsync("JoinDashboard");
        await agentConnection.InvokeAsync("JoinDashboard");
        await adminConnection.InvokeAsync("JoinDashboard");

        await NotifyAsync(userTicket);
        await ReadTicketAsync(userUpdates.Reader, userTicket.Id);
        await ReadTicketAsync(adminUpdates.Reader, userTicket.Id);
        await AssertNoTicketAsync(agentUpdates.Reader, userTicket.Id);

        await NotifyAsync(agentTicket);
        await ReadTicketAsync(agentUpdates.Reader, agentTicket.Id);
        await ReadTicketAsync(adminUpdates.Reader, agentTicket.Id);
        await AssertNoTicketAsync(userUpdates.Reader, agentTicket.Id);

        await NotifyAsync(unrelatedTicket);
        await ReadTicketAsync(adminUpdates.Reader, unrelatedTicket.Id);
        await AssertNoTicketAsync(userUpdates.Reader, unrelatedTicket.Id);
        await AssertNoTicketAsync(agentUpdates.Reader, unrelatedTicket.Id);
    }

    [Fact]
    public async Task DashboardUpdates_RemainIsolatedBetweenNormalAndDemoWorkspaces()
    {
        var normalTicket = await SeedTicketAsync(
            $"signalr-dashboard-normal-{Guid.NewGuid():N}");
        var demoTicket = await SeedTicketAsync(
            $"signalr-dashboard-demo-{Guid.NewGuid():N}",
            demoExpiresAtUtc: DateTime.UtcNow.AddHours(1));
        var normalUpdates = Channel.CreateUnbounded<TicketLiveUpdateDto>();
        var demoUpdates = Channel.CreateUnbounded<TicketLiveUpdateDto>();

        await using var normalConnection = await ConnectAsync(
            await LoginAsync("admin", CustomWebApplicationFactory.AdminPassword),
            normalUpdates.Writer);
        await using var demoConnection = await ConnectAsync(
            await GetDemoAdminTokenAsync(),
            demoUpdates.Writer);

        await normalConnection.InvokeAsync("JoinDashboard");
        await demoConnection.InvokeAsync("JoinDashboard");

        await NotifyAsync(normalTicket);
        await ReadTicketAsync(normalUpdates.Reader, normalTicket.Id);
        await AssertNoTicketAsync(demoUpdates.Reader, normalTicket.Id);

        await NotifyAsync(demoTicket);
        await ReadTicketAsync(demoUpdates.Reader, demoTicket.Id);
        await AssertNoTicketAsync(normalUpdates.Reader, demoTicket.Id);
    }

    private async Task AssertCanJoinAndReceiveAsync(string token, Ticket ticket)
    {
        var updates = Channel.CreateUnbounded<TicketLiveUpdateDto>();
        await using var connection = await ConnectAsync(token, updates.Writer);

        await connection.InvokeAsync("JoinTicket", ticket.Id.ToString());
        await NotifyAsync(ticket);

        var update = await ReadTicketAsync(updates.Reader, ticket.Id);
        update.TicketId.Should().Be(ticket.Id);
    }

    private async Task<HubConnection> ConnectAsync(
        string token,
        ChannelWriter<TicketLiveUpdateDto>? updates = null)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/tickets", options =>
            {
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();

        if (updates is not null)
        {
            connection.On<TicketLiveUpdateDto>(
                "TicketChanged",
                update => updates.TryWrite(update));
        }

        await connection.StartAsync();
        return connection;
    }

    private async Task<string> LoginAsync(string userName, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            UserName = userName,
            Password = password,
            DeviceName = "TicketsHubAuthorizationTests"
        });

        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<TokenResponseDto>();
        return token!.AccessToken;
    }

    private async Task<string> GetDemoAdminTokenAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync("demo-admin");

        if (user is null)
        {
            user = new ApplicationUser
            {
                Id = "demo-admin",
                UserName = "demo-admin",
                DisplayName = "Demo Admin",
                IsActive = true,
                IsDemoWorkspace = true,
                IsDemoUser = true,
                CreatedAtUtc = DateTime.UtcNow
            };
            (await userManager.CreateAsync(user)).Succeeded.Should().BeTrue();
            (await userManager.AddToRoleAsync(user, "Admin"))
                .Succeeded.Should().BeTrue();
        }

        var tokenService = scope.ServiceProvider
            .GetRequiredService<TokenService>();
        return (await tokenService.CreateAccessTokenAsync(user)).token;
    }

    private async Task<string> GetUserIdAsync(string userName)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users
            .Where(user => user.UserName == userName)
            .Select(user => user.Id)
            .SingleAsync();
    }

    private async Task<Ticket> SeedTicketAsync(
        string title,
        string? requesterUserId = null,
        string? assignedToUserId = null,
        DateTime? demoExpiresAtUtc = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ticket = new Ticket
        {
            Number = $"HDH-{Guid.NewGuid():N}",
            Title = title,
            Description = title,
            Status = "New",
            Priority = "Medium",
            CreatedAtUtc = DateTime.UtcNow,
            RequesterUserId = requesterUserId,
            AssignedToUserId = assignedToUserId,
            DemoExpiresAtUtc = demoExpiresAtUtc,
            RowVersion = [1]
        };

        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();
        return ticket;
    }

    private async Task NotifyAsync(Ticket ticket)
    {
        using var scope = _factory.Services.CreateScope();
        var notifier = scope.ServiceProvider.GetRequiredService<ITicketLiveNotifier>();
        await notifier.NotifyTicketChangedAsync(new TicketLiveUpdateDto
        {
            TicketId = ticket.Id,
            EventType = "Updated",
            Status = ticket.Status,
            Priority = ticket.Priority,
            AssignedToUserId = ticket.AssignedToUserId,
            ChangedAtUtc = DateTime.UtcNow
        });
    }

    private static async Task<TicketLiveUpdateDto> ReadTicketAsync(
        ChannelReader<TicketLiveUpdateDto> updates,
        int ticketId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (await updates.WaitToReadAsync(timeout.Token))
        {
            while (updates.TryRead(out var update))
            {
                if (update.TicketId == ticketId)
                    return update;
            }
        }

        throw new TimeoutException($"Ticket update {ticketId} was not received.");
    }

    private static async Task AssertNoTicketAsync(
        ChannelReader<TicketLiveUpdateDto> updates,
        int ticketId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        try
        {
            while (await updates.WaitToReadAsync(timeout.Token))
            {
                while (updates.TryRead(out var update))
                {
                    update.TicketId.Should().NotBe(ticketId);
                }
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
        }
    }
}
