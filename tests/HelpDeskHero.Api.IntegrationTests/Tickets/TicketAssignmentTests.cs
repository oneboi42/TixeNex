using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using HelpDeskHero.Api.Domain;
using HelpDeskHero.Api.Infrastructure.Persistence;
using HelpDeskHero.Shared.Contracts.Auth;
using HelpDeskHero.Shared.Contracts.Common;
using HelpDeskHero.Shared.Contracts.Tickets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HelpDeskHero.Api.IntegrationTests.Infrastructure;




namespace HelpDeskHero.Api.IntegrationTests.Tickets;

[Collection("ApiIntegration")]
public sealed class TicketAssignmentTests
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public TicketAssignmentTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("New", false)]
    [InlineData("InProgress", false)]
    [InlineData("Resolved", true)]
    public async Task Assign_ResetsSupportedStatusToNew(string sourceStatus, bool hasResolvedAt)
    {
        var ticket = await SeedTicketAsync(sourceStatus, resolved: hasResolvedAt);
        var agentId = await GetUserIdAsync("agent");
        await LoginAsync("admin");

        var response = await AssignAsync(ticket, agentId);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var updated = await GetTicketAsync(ticket.Id);
        updated.Status.Should().Be("New");
        updated.AssignedToUserId.Should().Be(agentId);
        updated.ResolvedAtUtc.Should().BeNull();
        updated.FirstRespondedAtUtc.Should().Be(ticket.FirstRespondedAtUtc);
        updated.DueFirstResponseAtUtc.Should().Be(ticket.DueFirstResponseAtUtc);
        updated.DueResolveAtUtc.Should().Be(ticket.DueResolveAtUtc);
        updated.EscalationLevel.Should().Be(ticket.EscalationLevel);
        updated.LastNotifiedAtUtc.Should().Be(ticket.LastNotifiedAtUtc);
    }

    [Fact]
    public async Task Reassign_InProgress_ResetsStatusAndWritesAuditAndOutbox()
    {
        var previousAgentId = await GetUserIdAsync("agent1");
        var newAgentId = await GetUserIdAsync("agent");
        var ticket = await SeedTicketAsync("InProgress", previousAgentId);
        await LoginAsync("admin");

        var response = await AssignAsync(ticket, newAgentId);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var audit = await db.AuditLogs.SingleAsync(x => x.EntityId == ticket.Id.ToString() && x.Action == "Reassign");
        using var auditJson = JsonDocument.Parse(audit.DetailsJson!);
        auditJson.RootElement.GetProperty("PreviousAssignedToUserId").GetString().Should().Be(previousAgentId);
        auditJson.RootElement.GetProperty("AssignedToUserId").GetString().Should().Be(newAgentId);
        auditJson.RootElement.GetProperty("AssignedToDisplayName").GetString().Should().NotBeNullOrWhiteSpace();
        auditJson.RootElement.GetProperty("PreviousStatus").GetString().Should().Be("InProgress");
        auditJson.RootElement.GetProperty("NewStatus").GetString().Should().Be("New");

        var outbox = await db.OutboxMessages.SingleAsync(x => x.Type == "TicketChanged" && x.Payload.Contains($"\"TicketId\":{ticket.Id}"));
        using var outboxJson = JsonDocument.Parse(outbox.Payload);
        outboxJson.RootElement.GetProperty("EventType").GetString().Should().Be("Reassigned");
        outboxJson.RootElement.GetProperty("Status").GetString().Should().Be("New");
        outboxJson.RootElement.GetProperty("AssignedToUserId").GetString().Should().Be(newAgentId);
    }

    [Fact]
    public async Task Assign_ClosedTicket_ReturnsBusinessConflictWithoutSideEffects()
    {
        var previousAgentId = await GetUserIdAsync("agent1");
        var newAgentId = await GetUserIdAsync("agent");
        var ticket = await SeedTicketAsync("Closed", previousAgentId, resolved: true);
        var originalUpdatedAt = ticket.UpdatedAtUtc;
        await LoginAsync("admin");

        var response = await AssignAsync(ticket, newAgentId);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("code").GetString().Should().Be("closed_ticket_cannot_be_assigned");
        var updated = await GetTicketAsync(ticket.Id);
        updated.Status.Should().Be("Closed");
        updated.AssignedToUserId.Should().Be(previousAgentId);
        updated.UpdatedAtUtc.Should().Be(originalUpdatedAt);
        await AssertNoWorkAsync(ticket.Id);
    }

    [Fact]
    public async Task Assign_SameAgent_IsNoOpAndDoesNotResetStatus()
    {
        var agentId = await GetUserIdAsync("agent");
        var ticket = await SeedTicketAsync("InProgress", agentId);
        var originalUpdatedAt = ticket.UpdatedAtUtc;
        await LoginAsync("admin");

        var response = await AssignAsync(ticket, agentId);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var updated = await GetTicketAsync(ticket.Id);
        updated.Status.Should().Be("InProgress");
        updated.UpdatedAtUtc.Should().Be(originalUpdatedAt);
        await AssertNoWorkAsync(ticket.Id);
    }

    [Fact]
    public async Task NewlyAssignedAgent_CanListRetrieveAndStartTicket()
    {
        var agentId = await GetUserIdAsync("agent");
        var ticket = await SeedTicketAsync("Resolved", resolved: true);
        await LoginAsync("admin");
        (await AssignAsync(ticket, agentId)).EnsureSuccessStatusCode();

        await LoginAsync("agent");
        var page = await _client.GetFromJsonAsync<PagedResultDto<TicketDto>>($"/api/tickets?search={ticket.Number}");
        page!.Items.Should().ContainSingle(x => x.Id == ticket.Id && x.Status == "New");
        var details = await _client.GetFromJsonAsync<TicketDto>($"/api/tickets/{ticket.Id}");
        details!.CanStart.Should().BeTrue();

        var start = await _client.PostAsJsonAsync($"/api/tickets/{ticket.Id}/start", new TicketLifecycleRequestDto
        {
            RowVersionBase64 = details.RowVersionBase64
        });
        start.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Assign_WithStaleRowVersion_ReturnsConcurrencyConflict()
    {
        var agentId = await GetUserIdAsync("agent");
        var ticket = await SeedTicketAsync("New");
        await LoginAsync("admin");

        var response = await _client.PostAsJsonAsync($"/api/tickets/{ticket.Id}/assign", new AssignTicketDto
        {
            AssignedToUserId = agentId,
            RowVersionBase64 = Convert.ToBase64String([99])
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("code").GetString().Should().Be("concurrency_conflict");
        (await GetTicketAsync(ticket.Id)).AssignedToUserId.Should().BeNull();
    }

    private async Task<HttpResponseMessage> AssignAsync(Ticket ticket, string agentId) =>
        await _client.PostAsJsonAsync($"/api/tickets/{ticket.Id}/assign", new AssignTicketDto
        {
            AssignedToUserId = agentId,
            RowVersionBase64 = Convert.ToBase64String(ticket.RowVersion)
        });

    private async Task<Ticket> SeedTicketAsync(string status, string? assignedToUserId = null, bool resolved = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var ticket = new Ticket
        {
            Number = $"HDH-{Guid.NewGuid():N}",
            Title = "Assignment workflow test",
            Description = "Assignment workflow test",
            Status = status,
            Priority = "Medium",
            CreatedAtUtc = now.AddHours(-2),
            UpdatedAtUtc = now.AddHours(-1),
            FirstRespondedAtUtc = now.AddMinutes(-50),
            DueFirstResponseAtUtc = now.AddMinutes(-40),
            DueResolveAtUtc = now.AddMinutes(40),
            ResolvedAtUtc = resolved ? now.AddMinutes(-10) : null,
            EscalationLevel = 2,
            LastNotifiedAtUtc = now.AddMinutes(-20),
            AssignedToUserId = assignedToUserId,
            RowVersion = [1]
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();
        return ticket;
    }

    private async Task<Ticket> GetTicketAsync(int id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Tickets.AsNoTracking().SingleAsync(x => x.Id == id);
    }

    private async Task AssertNoWorkAsync(int ticketId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.AuditLogs.AnyAsync(x => x.EntityId == ticketId.ToString() && (x.Action == "Assign" || x.Action == "Reassign"))).Should().BeFalse();
        (await db.OutboxMessages.AnyAsync(x => x.Payload.Contains($"\"TicketId\":{ticketId}"))).Should().BeFalse();
        (await db.UserNotifications.AnyAsync()).Should().BeFalse();
    }

    private async Task<string> GetUserIdAsync(string userName)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users.Where(x => x.UserName == userName).Select(x => x.Id).SingleAsync();
    }

    private async Task LoginAsync(string userName)
    {
        var password = userName switch
        {
            "admin" => "Admin1234",
            "agent" => "Agent123!",
            _ => throw new ArgumentOutOfRangeException(nameof(userName))
        };
        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            UserName = userName,
            Password = password,
            DeviceName = nameof(TicketAssignmentTests)
        });
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<TokenResponseDto>();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
    }
}
