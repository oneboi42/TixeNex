using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using HelpDeskHero.Api.Domain;
using HelpDeskHero.Api.Infrastructure.Persistence;
using HelpDeskHero.Shared.Contracts.Auth;
using HelpDeskHero.Shared.Contracts.Common;
using HelpDeskHero.Shared.Contracts.Tickets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDeskHero.Api.IntegrationTests;

[Collection("ApiIntegration")]
public sealed class TicketLifecycleAuthorizationTests
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public TicketLifecycleAuthorizationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("agent", "user", "agent", HttpStatusCode.NoContent)]
    [InlineData("agent1", "user", "agent", HttpStatusCode.Forbidden)]
    [InlineData("agent", "agent", "agent1", HttpStatusCode.Forbidden)]
    [InlineData("user", "user", "agent", HttpStatusCode.Forbidden)]
    [InlineData("admin", null, null, HttpStatusCode.NoContent)]
    [InlineData("agent", "user", null, HttpStatusCode.Forbidden)]
    public async Task Start_EnforcesAssignedAgentOrAdmin(
        string actor,
        string? requester,
        string? assigned,
        HttpStatusCode expectedStatus)
    {
        var ticket = await SeedTicketAsync(
            "New",
            await ResolveUserIdAsync(requester),
            await ResolveUserIdAsync(assigned));
        await LoginAsync(actor);

        var response = await PostLifecycleAsync(ticket, "start");

        response.StatusCode.Should().Be(expectedStatus);
        var saved = await FindTicketAsync(ticket.Id);
        saved.Status.Should().Be(expectedStatus == HttpStatusCode.NoContent ? "InProgress" : "New");
        if (expectedStatus == HttpStatusCode.NoContent)
            saved.FirstRespondedAtUtc.Should().NotBeNull();
    }

    [Theory]
    [InlineData("agent", "user", "agent", HttpStatusCode.NoContent)]
    [InlineData("agent1", "user", "agent", HttpStatusCode.Forbidden)]
    [InlineData("agent", "agent", "agent1", HttpStatusCode.Forbidden)]
    [InlineData("user", "user", "agent", HttpStatusCode.Forbidden)]
    [InlineData("admin", null, null, HttpStatusCode.NoContent)]
    public async Task Resolve_EnforcesAssignedAgentOrAdmin(
        string actor,
        string? requester,
        string? assigned,
        HttpStatusCode expectedStatus)
    {
        var ticket = await SeedTicketAsync(
            "InProgress",
            await ResolveUserIdAsync(requester),
            await ResolveUserIdAsync(assigned));
        await LoginAsync(actor);

        var response = await PostLifecycleAsync(ticket, "resolve");

        response.StatusCode.Should().Be(expectedStatus);
        var saved = await FindTicketAsync(ticket.Id);
        saved.Status.Should().Be(expectedStatus == HttpStatusCode.NoContent ? "Resolved" : "InProgress");
        if (expectedStatus == HttpStatusCode.NoContent)
            saved.ResolvedAtUtc.Should().NotBeNull();
    }

    [Theory]
    [InlineData("user", "user", "agent", HttpStatusCode.NoContent)]
    [InlineData("agent", "agent", "agent1", HttpStatusCode.NoContent)]
    [InlineData("agent", "user", "agent", HttpStatusCode.Forbidden)]
    [InlineData("user", "agent1", "agent", HttpStatusCode.Forbidden)]
    [InlineData("admin", null, null, HttpStatusCode.NoContent)]
    [InlineData("agent", null, "agent", HttpStatusCode.Forbidden)]
    public async Task Close_EnforcesRequesterOrAdmin(
        string actor,
        string? requester,
        string? assigned,
        HttpStatusCode expectedStatus)
    {
        var resolvedAtUtc = DateTime.UtcNow.AddMinutes(-5);
        var ticket = await SeedTicketAsync(
            "Resolved",
            await ResolveUserIdAsync(requester),
            await ResolveUserIdAsync(assigned),
            resolvedAtUtc);
        await LoginAsync(actor);

        var response = await PostLifecycleAsync(ticket, "close");

        response.StatusCode.Should().Be(expectedStatus);
        var saved = await FindTicketAsync(ticket.Id);
        saved.Status.Should().Be(expectedStatus == HttpStatusCode.NoContent ? "Closed" : "Resolved");
        saved.ResolvedAtUtc.Should().Be(resolvedAtUtc);
    }

    [Theory]
    [InlineData("user", "user", "agent", HttpStatusCode.NoContent)]
    [InlineData("agent", "agent", "agent1", HttpStatusCode.NoContent)]
    [InlineData("agent", "user", "agent", HttpStatusCode.Forbidden)]
    [InlineData("admin", null, null, HttpStatusCode.NoContent)]
    public async Task Reopen_EnforcesRequesterOrAdminAndClearsResolution(
        string actor,
        string? requester,
        string? assigned,
        HttpStatusCode expectedStatus)
    {
        var ticket = await SeedTicketAsync(
            "Resolved",
            await ResolveUserIdAsync(requester),
            await ResolveUserIdAsync(assigned),
            DateTime.UtcNow.AddMinutes(-5));
        await LoginAsync(actor);

        var response = await PostLifecycleAsync(ticket, "reopen");

        response.StatusCode.Should().Be(expectedStatus);
        var saved = await FindTicketAsync(ticket.Id);
        saved.Status.Should().Be(expectedStatus == HttpStatusCode.NoContent ? "InProgress" : "Resolved");
        if (expectedStatus == HttpStatusCode.NoContent)
            saved.ResolvedAtUtc.Should().BeNull();
    }

    [Theory]
    [InlineData("start", "InProgress")]
    [InlineData("resolve", "New")]
    [InlineData("close", "InProgress")]
    [InlineData("reopen", "Closed")]
    public async Task LifecycleEndpoint_RejectsInvalidStatus(string action, string status)
    {
        var ticket = await SeedTicketAsync(status, null, null);
        await LoginAsync("admin");

        var response = await PostLifecycleAsync(ticket, action);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await FindTicketAsync(ticket.Id)).Status.Should().Be(status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64")]
    public async Task LifecycleEndpoint_RejectsMissingOrInvalidRowVersion(string rowVersion)
    {
        var ticket = await SeedTicketAsync("New", null, null);
        await LoginAsync("admin");

        var response = await _client.PostAsJsonAsync(
            $"/api/tickets/{ticket.Id}/start",
            new TicketLifecycleRequestDto { RowVersionBase64 = rowVersion });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Start_WithStaleRowVersion_ReturnsConflict()
    {
        var ticket = await SeedTicketAsync("New", null, null);
        await LoginAsync("admin");

        var response = await _client.PostAsJsonAsync(
            $"/api/tickets/{ticket.Id}/start",
            new TicketLifecycleRequestDto
            {
                RowVersionBase64 = Convert.ToBase64String([99])
            });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("agent", "user", "agent", false, HttpStatusCode.NoContent)]
    [InlineData("agent1", "user", "agent", false, HttpStatusCode.Forbidden)]
    [InlineData("agent", "agent", "agent1", false, HttpStatusCode.Forbidden)]
    [InlineData("user", "user", "agent", false, HttpStatusCode.Forbidden)]
    [InlineData("admin", "user", "agent", false, HttpStatusCode.NoContent)]
    [InlineData("agent", "user", "agent", true, HttpStatusCode.Conflict)]
    public async Task Update_EnforcesContentEditorAndRejectsStatusChanges(
        string actor,
        string requester,
        string assigned,
        bool changeStatus,
        HttpStatusCode expectedStatus)
    {
        var ticket = await SeedTicketAsync(
            "New",
            await ResolveUserIdAsync(requester),
            await ResolveUserIdAsync(assigned));
        await LoginAsync(actor);

        var response = await _client.PutAsJsonAsync(
            $"/api/tickets/{ticket.Id}",
            new UpdateTicketDto
            {
                Title = $"Edited-{Guid.NewGuid():N}",
                Description = "Edited ticket description",
                Priority = "High",
                Status = changeStatus ? "InProgress" : "New",
                RowVersionBase64 = Convert.ToBase64String(ticket.RowVersion)
            });

        response.StatusCode.Should().Be(expectedStatus);
        var saved = await FindTicketAsync(ticket.Id);
        saved.Status.Should().Be("New");
        if (expectedStatus == HttpStatusCode.NoContent)
            saved.Priority.Should().Be("High");
    }

    [Fact]
    public async Task TicketCapabilities_SeparateRequesterAndAssignedAgentPermissions()
    {
        var agentAId = await GetUserIdAsync("agent");
        var agentBId = await GetUserIdAsync("agent1");
        var ticket = await SeedTicketAsync(
            "Resolved",
            agentAId,
            agentBId,
            DateTime.UtcNow.AddMinutes(-5));

        await LoginAsync("agent");
        var requesterView = await _client.GetFromJsonAsync<TicketDto>($"/api/tickets/{ticket.Id}");
        requesterView!.CanEdit.Should().BeFalse();
        requesterView.CanClose.Should().BeTrue();
        requesterView.CanReopen.Should().BeTrue();
        var requesterList = await _client.GetFromJsonAsync<PagedResultDto<TicketDto>>(
            $"/api/tickets?search={Uri.EscapeDataString(ticket.Title)}");
        requesterList!.Items.Single().Should().BeEquivalentTo(requesterView);

        await LoginAsync("agent1");
        var assigneeView = await _client.GetFromJsonAsync<TicketDto>($"/api/tickets/{ticket.Id}");
        assigneeView!.CanEdit.Should().BeTrue();
        assigneeView.CanClose.Should().BeFalse();
        assigneeView.CanReopen.Should().BeFalse();

        await LoginAsync("admin");
        var adminView = await _client.GetFromJsonAsync<TicketDto>($"/api/tickets/{ticket.Id}");
        adminView!.CanEdit.Should().BeTrue();
        adminView.CanClose.Should().BeTrue();
        adminView.CanReopen.Should().BeTrue();
    }

    private async Task LoginAsync(string userName)
    {
        var password = userName switch
        {
            "admin" => "Admin1234",
            "user" => "User123!",
            _ => "Agent123!"
        };
        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            UserName = userName,
            Password = password,
            DeviceName = "TicketLifecycleAuthorizationTests"
        });

        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<TokenResponseDto>();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token!.AccessToken);
    }

    private async Task<string?> ResolveUserIdAsync(string? userName) =>
        userName is null ? null : await GetUserIdAsync(userName);

    private async Task<string> GetUserIdAsync(string userName)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users
            .Where(x => x.UserName == userName)
            .Select(x => x.Id)
            .SingleAsync();
    }

    private async Task<Ticket> SeedTicketAsync(
        string status,
        string? requesterUserId,
        string? assignedToUserId,
        DateTime? resolvedAtUtc = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ticket = new Ticket
        {
            Number = $"HDH-{Guid.NewGuid():N}",
            Title = $"Lifecycle-{Guid.NewGuid():N}",
            Description = "Lifecycle authorization test ticket",
            Status = status,
            Priority = "Medium",
            CreatedAtUtc = DateTime.UtcNow,
            RequesterUserId = requesterUserId,
            AssignedToUserId = assignedToUserId,
            ResolvedAtUtc = resolvedAtUtc,
            RowVersion = [1]
        };

        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();
        return ticket;
    }

    private async Task<Ticket> FindTicketAsync(int id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Tickets.AsNoTracking().SingleAsync(x => x.Id == id);
    }

    private Task<HttpResponseMessage> PostLifecycleAsync(Ticket ticket, string action)
    {
        return _client.PostAsJsonAsync(
            $"/api/tickets/{ticket.Id}/{action}",
            new TicketLifecycleRequestDto
            {
                RowVersionBase64 = Convert.ToBase64String(ticket.RowVersion)
            });
    }
}
