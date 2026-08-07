using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using HelpDeskHero.Api.Infrastructure.Persistence;
using HelpDeskHero.Api.IntegrationTests.Infrastructure;
using HelpDeskHero.Shared.Contracts.Auth;
using HelpDeskHero.Shared.Contracts.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDeskHero.Api.IntegrationTests.Users;

[Collection("ApiIntegration")]
public sealed class UsersControllerTests
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public UsersControllerTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
        _factory = factory;
    }

    [Fact]
    public async Task GetAgents_AsNormalAdmin_ReturnsOnlyNormalWorkspaceAgents()
    {
        var demoAgent = await CreateDemoSessionAsync("Agent");
        await LoginAsync("admin", "Admin1234");

        var agents = await GetAgentsAsync();

        agents.Should().Contain(x => x.UserName == "agent");
        agents.Should().OnlyContain(x => !x.UserName.StartsWith("demo-"));
        agents.Should().NotContain(x => x.UserName == demoAgent.UserName);
    }

    [Fact]
    public async Task GetAgents_AsDemoAdmin_ReturnsOnlyDemoWorkspaceAgents()
    {
        var demoAgent = await CreateDemoSessionAsync("Agent");
        var demoAdmin = await CreateDemoSessionAsync("Admin");
        UseToken(demoAdmin.AccessToken);

        var agents = await GetAgentsAsync();

        agents.Should().ContainSingle(x => x.UserName == demoAgent.UserName);
        agents.Should().OnlyContain(x => x.UserName.StartsWith("demo-agent-"));
        agents.Should().NotContain(x => x.UserName == "agent");
    }

    [Fact]
    public async Task GetAgents_ExcludesInactiveAgentWithinCallerWorkspace()
    {
        var demoAgent = await CreateDemoSessionAsync("Agent");
        await SetUserActiveAsync(demoAgent.UserName, isActive: false);
        var demoAdmin = await CreateDemoSessionAsync("Admin");
        UseToken(demoAdmin.AccessToken);

        var agents = await GetAgentsAsync();

        agents.Should().NotContain(x => x.UserName == demoAgent.UserName);
    }

    private async Task<IReadOnlyList<AssignableAgentDto>> GetAgentsAsync()
    {
        return await _client.GetFromJsonAsync<List<AssignableAgentDto>>(
            "/api/users/agents") ?? [];
    }

    private async Task<TokenResponseDto> CreateDemoSessionAsync(string role)
    {
        var response = await _client.PostAsJsonAsync("/api/demo/sessions", new CreateDemoSessionRequestDto
        {
            Role = role,
            DeviceName = nameof(UsersControllerTests)
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TokenResponseDto>())!;
    }

    private async Task LoginAsync(string userName, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            UserName = userName,
            Password = password,
            DeviceName = nameof(UsersControllerTests)
        });
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<TokenResponseDto>();
        UseToken(token!.AccessToken);
    }

    private void UseToken(string accessToken)
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);
    }

    private async Task SetUserActiveAsync(string userName, bool isActive)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(x => x.UserName == userName);
        user.IsActive = isActive;
        await db.SaveChangesAsync();
    }
}
