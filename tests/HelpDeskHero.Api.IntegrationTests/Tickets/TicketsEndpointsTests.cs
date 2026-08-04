using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using HelpDeskHero.Shared.Contracts.Auth;
using HelpDeskHero.Shared.Contracts.Common;
using HelpDeskHero.Shared.Contracts.Tickets;
using HelpDeskHero.Api.Domain;
using HelpDeskHero.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HelpDeskHero.Api.IntegrationTests.Infrastructure;

namespace HelpDeskHero.Api.IntegrationTests.Tickets;

[Collection("ApiIntegration")]
public sealed class TicketsEndpointsTests
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public TicketsEndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetTickets_WithoutToken_ShouldReturnUnauthorized()
    {
        var response = await _client.GetAsync("/api/tickets");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetTickets_AsAdmin_ShouldReturnSuccess()
    {
        await LoginAsAdminAsync();

        var response = await _client.GetAsync("/api/tickets");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResultDto<TicketDto>>();
        result.Should().NotBeNull();
        result!.Items.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateTicket_AsAdmin_ShouldReturnCreated()
    {
        await LoginAsAdminAsync();

        var dto = new CreateTicketDto
        {
            Title = "Integration test ticket",
            Description = "Created by integration test",
            Priority = "High"
        };

        var response = await _client.PostAsJsonAsync("/api/tickets", dto);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<TicketDto>();
        created.Should().NotBeNull();
        created!.Title.Should().Be(dto.Title);
        created.Priority.Should().Be(dto.Priority);
        created.Status.Should().Be("New");
        created.RequesterDisplayName.Should().Be("System Admin");
    }

    [Fact]
    public async Task CreateTicket_AsUser_ShouldSaveRequesterAndAutomaticAssignment()
    {
        var userId = await LoginAsync("user", "User123!");
        var title = $"User ownership {Guid.NewGuid():N}";

        var response = await CreateTicketAsync(title);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<TicketDto>();
        created!.RequesterDisplayName.Should().BeNull();
        var ticket = await FindTicketByTitleAsync(title);
        ticket.RequesterUserId.Should().Be(userId);
        ticket.AssignedToUserId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CreateTicket_AsAgent_ShouldKeepRequesterAndAssignmentIndependent()
    {
        var agentId = await LoginAsync("agent", "Agent123!");
        var title = $"Agent ownership {Guid.NewGuid():N}";

        var response = await CreateTicketAsync(title);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var ticket = await FindTicketByTitleAsync(title);
        ticket.RequesterUserId.Should().Be(agentId);
        ticket.AssignedToUserId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CreateTicket_AsAdmin_ShouldSaveAdminAsRequester()
    {
        var adminId = await LoginAsync("admin", "Admin1234");
        var title = $"Admin ownership {Guid.NewGuid():N}";

        var response = await CreateTicketAsync(title);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var ticket = await FindTicketByTitleAsync(title);
        ticket.RequesterUserId.Should().Be(adminId);
    }

    private async Task LoginAsAdminAsync()
    {
        await LoginAsync("admin", "Admin1234");
    }

    private async Task<string> LoginAsync(string userName, string password)
    {
        var login = new LoginRequestDto
        {
            UserName = userName,
            Password = password,
            DeviceName = "IntegrationTests"
        };

        var response = await _client.PostAsJsonAsync("/api/auth/login", login);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var token = await response.Content.ReadFromJsonAsync<TokenResponseDto>();
        token.Should().NotBeNull();
        token!.AccessToken.Should().NotBeNullOrWhiteSpace();

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token.AccessToken);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users
            .Where(x => x.UserName == userName)
            .Select(x => x.Id)
            .SingleAsync();
    }

    private async Task<HttpResponseMessage> CreateTicketAsync(string title)
    {
        return await _client.PostAsJsonAsync("/api/tickets", new CreateTicketDto
        {
            Title = title,
            Description = "Created by ownership integration test",
            Priority = "Medium"
        });
    }

    private async Task<Ticket> FindTicketByTitleAsync(string title)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Tickets.AsNoTracking().SingleAsync(x => x.Title == title);
    }
}
