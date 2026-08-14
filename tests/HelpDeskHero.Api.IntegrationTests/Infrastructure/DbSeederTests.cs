using FluentAssertions;
using HelpDeskHero.Api.Domain;
using HelpDeskHero.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDeskHero.Api.IntegrationTests.Infrastructure;

public sealed class DbSeederTests : IDisposable
{
    private readonly CustomWebApplicationFactory _factory = new();
    private readonly HttpClient _client;

    public DbSeederTests()
    {
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task SeedAsync_CreatesPersistentDemoAgentsIdempotently()
    {
        await DbSeeder.SeedAsync(_factory.Services);
        await DbSeeder.SeedAsync(_factory.Services);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var demoAgents = await db.Users
            .AsNoTracking()
            .Where(user => user.UserName == "demo-agent-1" || user.UserName == "demo-agent-2")
            .OrderBy(user => user.UserName)
            .ToListAsync();

        demoAgents.Should().HaveCount(2);
        demoAgents.Select(user => user.UserName).Should().Equal("demo-agent-1", "demo-agent-2");
        demoAgents.Select(user => user.DisplayName).Should().Equal("Demo Agent 1", "Demo Agent 2");
        demoAgents.Should().OnlyContain(user =>
            user.IsActive &&
            user.IsDemoWorkspace &&
            !user.IsDemoUser &&
            user.DemoExpiresAtUtc == null &&
            user.DemoAbsoluteExpiresAtUtc == null &&
            user.LastActivityAtUtc == null);

        foreach (var demoAgent in demoAgents)
        {
            (await userManager.IsInRoleAsync(demoAgent, "Agent")).Should().BeTrue();
        }

        var normalSeedUsers = await db.Users
            .AsNoTracking()
            .Where(user => new[] { "admin", "agent", "agent1", "agent2", "user" }.Contains(user.UserName!))
            .ToListAsync();

        normalSeedUsers.Should().HaveCount(5);
        normalSeedUsers.Should().OnlyContain(user => !user.IsDemoWorkspace && !user.IsDemoUser);
        (await db.Users.CountAsync(user => user.UserName == "demo-agent-1")).Should().Be(1);
        (await db.Users.CountAsync(user => user.UserName == "demo-agent-2")).Should().Be(1);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
