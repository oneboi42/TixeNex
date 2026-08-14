using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using FluentAssertions;
using HelpDeskHero.Api.Infrastructure.Persistence;
using HelpDeskHero.Api.IntegrationTests.Infrastructure;
using HelpDeskHero.Shared.Contracts.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDeskHero.Api.IntegrationTests.Auth;

[Collection("ApiIntegration")]
public sealed class DemoSessionTests
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public DemoSessionTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
        _factory = factory;
    }

    [Fact]
    public async Task CreateSession_MarksTemporaryUserAndIssuesWorkspaceClaim()
    {
        var response = await _client.PostAsJsonAsync("/api/demo/sessions", new CreateDemoSessionRequestDto
        {
            Role = "User",
            DeviceName = nameof(DemoSessionTests)
        });

        response.EnsureSuccessStatusCode();
        var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponseDto>();
        tokenResponse.Should().NotBeNull();

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(tokenResponse!.AccessToken);
        jwt.Claims.Single(x => x.Type == "is_demo_workspace").Value.Should().Be("true");
        jwt.Claims.Single(x => x.Type == "is_demo").Value.Should().Be("true");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users
            .AsNoTracking()
            .SingleAsync(x => x.UserName == tokenResponse.UserName);

        user.IsDemoWorkspace.Should().BeTrue();
        user.IsDemoUser.Should().BeTrue();
    }
}
