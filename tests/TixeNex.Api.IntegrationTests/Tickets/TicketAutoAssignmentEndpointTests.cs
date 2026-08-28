using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using TixeNex.Api.Infrastructure.Persistence;
using TixeNex.Api.IntegrationTests.Infrastructure;
using TixeNex.Shared.Contracts.Auth;
using TixeNex.Shared.Contracts.Tickets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace TixeNex.Api.IntegrationTests.Tickets;

public sealed class TicketAutoAssignmentEndpointTests : IDisposable
{
    private readonly CustomWebApplicationFactory _factory = new();
    private readonly HttpClient _client;

    public TicketAutoAssignmentEndpointTests()
    {
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task CreateDemoTicket_AutoAssignsPersistentDemoAgent()
    {
        var sessionResponse = await _client.PostAsJsonAsync(
            "/api/demo/sessions",
            new CreateDemoSessionRequestDto
            {
                Role = "User",
                DeviceName = nameof(TicketAutoAssignmentEndpointTests)
            });
        sessionResponse.EnsureSuccessStatusCode();
        var session = (await sessionResponse.Content.ReadFromJsonAsync<TokenResponseDto>())!;
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session.AccessToken);
        var title = $"Demo auto-assignment {Guid.NewGuid():N}";

        var response = await _client.PostAsJsonAsync(
            "/api/tickets",
            new CreateTicketDto
            {
                Title = title,
                Description = "Demo auto-assignment endpoint test",
                Priority = "Medium"
            });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ticket = await db.Tickets.AsNoTracking().SingleAsync(item => item.Title == title);
        var assignedUser = await db.Users.AsNoTracking()
            .SingleAsync(user => user.Id == ticket.AssignedToUserId);
        ticket.DemoExpiresAtUtc.Should().NotBeNull();
        assignedUser.UserName.Should().BeOneOf("demo-agent-1", "demo-agent-2");
        assignedUser.IsDemoWorkspace.Should().BeTrue();
        assignedUser.IsDemoUser.Should().BeFalse();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
