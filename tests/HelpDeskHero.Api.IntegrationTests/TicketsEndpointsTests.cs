using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using HelpDeskHero.Shared.Contracts.Auth;
using HelpDeskHero.Shared.Contracts.Common;
using HelpDeskHero.Shared.Contracts.Tickets;

namespace HelpDeskHero.Api.IntegrationTests;

public sealed class TicketsEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public TicketsEndpointsTests(CustomWebApplicationFactory factory)
    {
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
    }

    private async Task LoginAsAdminAsync()
    {
        var login = new LoginRequestDto
        {
            UserName = "admin",
            Password = "Admin1234",
            DeviceName = "IntegrationTests"
        };

        var response = await _client.PostAsJsonAsync("/api/auth/login", login);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var token = await response.Content.ReadFromJsonAsync<TokenResponseDto>();
        token.Should().NotBeNull();
        token!.AccessToken.Should().NotBeNullOrWhiteSpace();

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token.AccessToken);
    }
}
