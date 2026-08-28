using FluentAssertions;
using TixeNex.Api.Application.Interfaces;
using TixeNex.Api.Domain;
using TixeNex.Api.Infrastructure.Persistence;
using TixeNex.Api.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace TixeNex.Api.IntegrationTests.Tickets;

public sealed class TicketAssignmentServiceTests : IDisposable
{
    private readonly CustomWebApplicationFactory _factory = new();
    private readonly HttpClient _client;

    public TicketAssignmentServiceTests()
    {
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task AssignAsync_NormalTicket_UsesOnlyNormalWorkspaceAgents()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var temporaryDemoAgent = await CreateTemporaryDemoAgentAsync(userManager);
        var service = scope.ServiceProvider.GetRequiredService<ITicketAssignmentService>();
        var ticket = CreateTicket(isDemoWorkspace: false);

        var assignedUserId = await service.AssignAsync(ticket);

        var assignedUser = await userManager.FindByIdAsync(assignedUserId!);
        assignedUser.Should().NotBeNull();
        assignedUser!.IsDemoWorkspace.Should().BeFalse();
        assignedUser.Id.Should().NotBe(temporaryDemoAgent.Id);
        assignedUser.UserName.Should().NotBe("demo-agent-1");
        assignedUser.UserName.Should().NotBe("demo-agent-2");
    }

    [Fact]
    public async Task AssignAsync_DemoTicket_UsesPersistentDemoAgentsOnly()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var temporaryDemoAgent = await CreateTemporaryDemoAgentAsync(userManager);
        var service = scope.ServiceProvider.GetRequiredService<ITicketAssignmentService>();
        var ticket = CreateTicket(isDemoWorkspace: true);

        var assignedUserId = await service.AssignAsync(ticket);

        var assignedUser = await userManager.FindByIdAsync(assignedUserId!);
        assignedUser.Should().NotBeNull();
        assignedUser!.IsDemoWorkspace.Should().BeTrue();
        assignedUser.IsDemoUser.Should().BeFalse();
        assignedUser.Id.Should().NotBe(temporaryDemoAgent.Id);
        assignedUser.UserName.Should().BeOneOf("demo-agent-1", "demo-agent-2");
    }

    [Fact]
    public async Task AssignAsync_DemoTicket_UsesDemoLoadAndIgnoresNormalLoad()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var demoAgent1 = (await userManager.FindByNameAsync("demo-agent-1"))!;
        var demoAgent2 = (await userManager.FindByNameAsync("demo-agent-2"))!;

        db.Tickets.Add(CreateTicket(isDemoWorkspace: true, assignedToUserId: demoAgent1.Id));
        db.Tickets.AddRange(Enumerable.Range(0, 3)
            .Select(_ => CreateTicket(isDemoWorkspace: false, assignedToUserId: demoAgent2.Id)));
        await db.SaveChangesAsync();

        var service = scope.ServiceProvider.GetRequiredService<ITicketAssignmentService>();
        var assignedUserId = await service.AssignAsync(CreateTicket(isDemoWorkspace: true));

        assignedUserId.Should().Be(demoAgent2.Id);
    }

    [Fact]
    public async Task AssignAsync_NormalTicket_UsesNormalLoadAndIgnoresDemoLoad()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var normalAgents = (await userManager.GetUsersInRoleAsync("Agent"))
            .Where(user => !user.IsDemoWorkspace)
            .OrderBy(user => user.Id)
            .ToList();
        var expectedAgent = normalAgents[0];

        db.Tickets.AddRange(normalAgents.Skip(1)
            .Select(agent => CreateTicket(isDemoWorkspace: false, assignedToUserId: agent.Id)));
        db.Tickets.AddRange(Enumerable.Range(0, 3)
            .Select(_ => CreateTicket(isDemoWorkspace: true, assignedToUserId: expectedAgent.Id)));
        await db.SaveChangesAsync();

        var service = scope.ServiceProvider.GetRequiredService<ITicketAssignmentService>();
        var assignedUserId = await service.AssignAsync(CreateTicket(isDemoWorkspace: false));

        assignedUserId.Should().Be(expectedAgent.Id);
    }

    [Fact]
    public async Task AssignAsync_EqualLoads_UsesUserIdTieBreaker()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var expectedUserId = (await userManager.GetUsersInRoleAsync("Agent"))
            .Where(user => !user.IsDemoWorkspace)
            .Select(user => user.Id)
            .Order()
            .First();
        var service = scope.ServiceProvider.GetRequiredService<ITicketAssignmentService>();

        var assignedUserId = await service.AssignAsync(CreateTicket(isDemoWorkspace: false));

        assignedUserId.Should().Be(expectedUserId);
    }

    private static Ticket CreateTicket(bool isDemoWorkspace, string? assignedToUserId = null) =>
        new()
        {
            Number = $"T-{Guid.NewGuid():N}"[..22],
            Title = "Assignment service test",
            Description = "Assignment service test",
            Status = "New",
            Priority = "Medium",
            CreatedAtUtc = DateTime.UtcNow,
            AssignedToUserId = assignedToUserId,
            DemoExpiresAtUtc = isDemoWorkspace ? DateTime.UtcNow.AddHours(1) : null
        };

    private static async Task<ApplicationUser> CreateTemporaryDemoAgentAsync(
        UserManager<ApplicationUser> userManager)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var user = new ApplicationUser
        {
            UserName = $"temporary-demo-agent-{suffix}",
            Email = $"temporary-demo-agent-{suffix}@demo.tixenex.local",
            DisplayName = "Temporary Demo Agent",
            EmailConfirmed = true,
            IsActive = true,
            IsDemoWorkspace = true,
            IsDemoUser = true,
            DemoExpiresAtUtc = DateTime.UtcNow.AddMinutes(30),
            DemoAbsoluteExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            LastActivityAtUtc = DateTime.UtcNow
        };

        (await userManager.CreateAsync(user)).Succeeded.Should().BeTrue();
        (await userManager.AddToRoleAsync(user, "Agent")).Succeeded.Should().BeTrue();
        return user;
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
