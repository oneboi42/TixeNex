using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using HelpDeskHero.Api.Domain;
using HelpDeskHero.Api.Infrastructure.Persistence;
using HelpDeskHero.Shared.Contracts.Auth;
using HelpDeskHero.Shared.Contracts.Common;
using HelpDeskHero.Shared.Contracts.Tickets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using HelpDeskHero.Api.IntegrationTests.Infrastructure;

namespace HelpDeskHero.Api.IntegrationTests.Tickets;

[Collection("ApiIntegration")]
public sealed class TicketVisibilityTests
{
    private const string JwtIssuer = "HelpDeskHero.Tests";
    private const string JwtAudience = "HelpDeskHero.Tests";
    private const string JwtKey = "HelpDeskHero.Tests.Super.Secret.Key.For.Jwt.Token.Signing.123456789";

    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public TicketVisibilityTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetTickets_AsUser_ReturnsOnlyRequestedTickets()
    {
        var userAId = await GetUserIdAsync("user");
        var userBId = await GetUserIdAsync("agent1");
        var marker = $"user-list-{Guid.NewGuid():N}";
        var tickets = await SeedTicketsAsync(
            new TicketSeed($"{marker}-own", userAId, null),
            new TicketSeed($"{marker}-other", userBId, null),
            new TicketSeed($"{marker}-legacy", null, null));
        await LoginAsync("user", "User123!");

        var result = await GetPageAsync(marker);

        result.TotalCount.Should().Be(1);
        result.Items.Select(x => x.Id).Should().Equal(tickets[0].Id);
    }

    [Fact]
    public async Task GetTickets_AsAgent_ReturnsAssignedAndRequestedTickets()
    {
        var agentAId = await GetUserIdAsync("agent");
        var agentBId = await GetUserIdAsync("agent1");
        var requesterId = await GetUserIdAsync("user");
        var marker = $"agent-list-{Guid.NewGuid():N}";
        var tickets = await SeedTicketsAsync(
            new TicketSeed($"{marker}-assigned-a", null, agentAId),
            new TicketSeed($"{marker}-assigned-b", requesterId, agentBId),
            new TicketSeed($"{marker}-requested-a", agentAId, null),
            new TicketSeed($"{marker}-unrelated", requesterId, null));
        await LoginAsync("agent", "Agent123!");

        var result = await GetPageAsync(marker);

        result.TotalCount.Should().Be(2);
        result.Items.Select(x => x.Id).Should()
            .BeEquivalentTo([tickets[0].Id, tickets[2].Id]);
    }

    [Fact]
    public async Task GetTickets_AsAdminWithAgentRole_ReturnsAllTickets()
    {
        var userId = await GetUserIdAsync("user");
        var agentAId = await GetUserIdAsync("agent");
        var agentBId = await GetUserIdAsync("agent1");
        var marker = $"admin-list-{Guid.NewGuid():N}";
        var tickets = await SeedTicketsAsync(
            new TicketSeed($"{marker}-first", userId, agentAId),
            new TicketSeed($"{marker}-second", agentAId, agentBId),
            new TicketSeed($"{marker}-legacy", null, agentAId),
            new TicketSeed($"{marker}-unassigned", userId, null));
        await LoginAsync("admin", "Admin1234");

        var result = await GetPageAsync(marker);

        result.TotalCount.Should().Be(4);
        result.Items.Select(x => x.Id).Should().BeEquivalentTo(tickets.Select(x => x.Id));
    }

    [Fact]
    public async Task GetTicketById_AsUser_HidesOtherAndLegacyTickets()
    {
        var userAId = await GetUserIdAsync("user");
        var userBId = await GetUserIdAsync("agent1");
        var marker = $"user-details-{Guid.NewGuid():N}";
        var tickets = await SeedTicketsAsync(
            new TicketSeed($"{marker}-own", userAId, null),
            new TicketSeed($"{marker}-other", userBId, null),
            new TicketSeed($"{marker}-legacy", null, null));
        await LoginAsync("user", "User123!");

        (await _client.GetAsync($"/api/tickets/{tickets[0].Id}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.GetAsync($"/api/tickets/{tickets[1].Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _client.GetAsync($"/api/tickets/{tickets[2].Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetTicketById_AsAgent_ReturnsAssignedAndRequestedTickets()
    {
        var agentAId = await GetUserIdAsync("agent");
        var agentBId = await GetUserIdAsync("agent1");
        var marker = $"agent-details-{Guid.NewGuid():N}";
        var tickets = await SeedTicketsAsync(
            new TicketSeed($"{marker}-assigned-a", null, agentAId),
            new TicketSeed($"{marker}-assigned-b", agentAId, agentBId),
            new TicketSeed($"{marker}-requested-a", agentAId, null),
            new TicketSeed($"{marker}-unrelated", null, null));
        await LoginAsync("agent", "Agent123!");

        (await _client.GetAsync($"/api/tickets/{tickets[0].Id}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.GetAsync($"/api/tickets/{tickets[1].Id}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.GetAsync($"/api/tickets/{tickets[2].Id}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.GetAsync($"/api/tickets/{tickets[3].Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetTicketById_AsAdmin_ReturnsMixedTickets()
    {
        var userId = await GetUserIdAsync("user");
        var agentId = await GetUserIdAsync("agent");
        var marker = $"admin-details-{Guid.NewGuid():N}";
        var tickets = await SeedTicketsAsync(
            new TicketSeed($"{marker}-legacy", null, agentId),
            new TicketSeed($"{marker}-unassigned", userId, null),
            new TicketSeed($"{marker}-other", userId, agentId));
        await LoginAsync("admin", "Admin1234");

        foreach (var ticket in tickets)
        {
            (await _client.GetAsync($"/api/tickets/{ticket.Id}"))
                .StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task TicketResponses_IncludeAssignedAgentAndPreserveCapabilities()
    {
        var agentId = await GetUserIdAsync("agent");
        var agentDisplayName = await GetDisplayNameAsync("agent");
        var marker = $"assignment-{Guid.NewGuid():N}";
        var tickets = await SeedTicketsAsync(
            new TicketSeed($"{marker}-assigned", null, agentId),
            new TicketSeed($"{marker}-unassigned", null, null));
        await LoginAsync("admin", "Admin1234");

        var list = await GetPageAsync(marker);
        var assignedListItem = list.Items.Single(x => x.Id == tickets[0].Id);
        var unassignedListItem = list.Items.Single(x => x.Id == tickets[1].Id);
        var assignedDetails = await _client.GetFromJsonAsync<TicketDto>(
            $"/api/tickets/{tickets[0].Id}");
        var unassignedDetails = await _client.GetFromJsonAsync<TicketDto>(
            $"/api/tickets/{tickets[1].Id}");

        assignedListItem.AssignedToUserId.Should().Be(agentId);
        assignedListItem.AssignedToDisplayName.Should().Be(agentDisplayName);
        assignedDetails.Should().NotBeNull();
        assignedDetails!.AssignedToUserId.Should().Be(agentId);
        assignedDetails.AssignedToDisplayName.Should().Be(agentDisplayName);

        unassignedListItem.AssignedToUserId.Should().BeNull();
        unassignedListItem.AssignedToDisplayName.Should().BeNull();
        unassignedDetails.Should().NotBeNull();
        unassignedDetails!.AssignedToUserId.Should().BeNull();
        unassignedDetails.AssignedToDisplayName.Should().BeNull();

        assignedListItem.CanEdit.Should().BeTrue();
        assignedListItem.CanStart.Should().BeTrue();
        assignedListItem.CanResolve.Should().BeFalse();
        assignedDetails.CanEdit.Should().Be(assignedListItem.CanEdit);
        assignedDetails.CanStart.Should().Be(assignedListItem.CanStart);
        assignedDetails.CanResolve.Should().Be(assignedListItem.CanResolve);
        assignedDetails.CanClose.Should().Be(assignedListItem.CanClose);
        assignedDetails.CanReopen.Should().Be(assignedListItem.CanReopen);
    }

    [Fact]
    public async Task TicketResponses_AsAdmin_IncludeRequesterDisplayName()
    {
        var requesterId = await GetUserIdAsync("user");
        var requesterDisplayName = await GetDisplayNameAsync("user");
        var marker = $"requester-admin-{Guid.NewGuid():N}";
        var ticket = (await SeedTicketsAsync(new TicketSeed(marker, requesterId, null))).Single();
        await LoginAsync("admin", "Admin1234");

        var listItem = (await GetPageAsync(marker)).Items.Single();
        var details = await _client.GetFromJsonAsync<TicketDto>($"/api/tickets/{ticket.Id}");

        listItem.RequesterDisplayName.Should().Be(requesterDisplayName);
        details!.RequesterDisplayName.Should().Be(requesterDisplayName);
    }

    [Theory]
    [InlineData("agent", "Agent123!")]
    [InlineData("user", "User123!")]
    public async Task TicketResponses_AsNonAdmin_OmitRequesterDisplayName(
        string userName,
        string password)
    {
        var actorId = await GetUserIdAsync(userName);
        var marker = $"requester-hidden-{userName}-{Guid.NewGuid():N}";
        var requesterId = userName == "user" ? actorId : await GetUserIdAsync("user");
        var assignedId = userName == "agent" ? actorId : null;
        var ticket = (await SeedTicketsAsync(new TicketSeed(marker, requesterId, assignedId))).Single();
        await LoginAsync(userName, password);

        var listItem = (await GetPageAsync(marker)).Items.Single();
        var details = await _client.GetFromJsonAsync<TicketDto>($"/api/tickets/{ticket.Id}");

        listItem.RequesterDisplayName.Should().BeNull();
        details!.RequesterDisplayName.Should().BeNull();
    }

    [Fact]
    public async Task LegacyTicket_AsAdmin_ReturnsNullRequesterWithoutError()
    {
        var marker = $"requester-legacy-{Guid.NewGuid():N}";
        var ticket = (await SeedTicketsAsync(new TicketSeed(marker, null, null))).Single();
        await LoginAsync("admin", "Admin1234");

        var listItem = (await GetPageAsync(marker)).Items.Single();
        var details = await _client.GetFromJsonAsync<TicketDto>($"/api/tickets/{ticket.Id}");

        listItem.RequesterDisplayName.Should().BeNull();
        details!.RequesterDisplayName.Should().BeNull();
    }

    [Fact]
    public async Task GetTickets_AppliesVisibilityBeforeFiltersPaginationAndCount()
    {
        var userAId = await GetUserIdAsync("user");
        var userBId = await GetUserIdAsync("agent1");
        var marker = $"filtered-{Guid.NewGuid():N}";
        var tickets = await SeedTicketsAsync(
            new TicketSeed($"{marker}-visible-1", userAId, null, "New", "High"),
            new TicketSeed($"{marker}-visible-2", userAId, null, "New", "High"),
            new TicketSeed($"{marker}-invisible", userBId, null, "New", "High"),
            new TicketSeed($"{marker}-wrong-status", userAId, null, "Closed", "High"),
            new TicketSeed($"{marker}-wrong-priority", userAId, null, "New", "Low"));
        await LoginAsync("user", "User123!");

        var firstPage = await GetPageAsync(marker, "status=New&priority=High&pageSize=1&pageNumber=1");
        var secondPage = await GetPageAsync(marker, "status=New&priority=High&pageSize=1&pageNumber=2");

        firstPage.TotalCount.Should().Be(2);
        secondPage.TotalCount.Should().Be(2);
        firstPage.Items.Concat(secondPage.Items).Select(x => x.Id)
            .Should().BeEquivalentTo(tickets.Take(2).Select(x => x.Id));
    }

    [Fact]
    public async Task GetTickets_WithoutNameIdentifier_ReturnsUnauthorized()
    {
        SetToken(new Claim(ClaimTypes.Role, "User"));

        var response = await _client.GetAsync("/api/tickets");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetTickets_WithUnsupportedRole_ReturnsForbidden()
    {
        SetToken(
            new Claim(ClaimTypes.NameIdentifier, "unsupported-user"),
            new Claim(ClaimTypes.Role, "Manager"));

        var response = await _client.GetAsync("/api/tickets");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("user", "User123!", "User")]
    [InlineData("agent", "Agent123!", "Agent")]
    [InlineData("admin", "Admin1234", "Admin")]
    public async Task SoftDeletedTickets_AreExcludedFromListAndDetails(
        string userName,
        string password,
        string role)
    {
        var currentUserId = await GetUserIdAsync(userName);
        var marker = $"deleted-{role}-{Guid.NewGuid():N}";
        var requesterUserId = role == "User" ? currentUserId : null;
        var assignedToUserId = role == "Agent" ? currentUserId : null;
        var ticket = (await SeedTicketsAsync(
            new TicketSeed(
                marker,
                requesterUserId,
                assignedToUserId,
                IsDeleted: true))).Single();
        await LoginAsync(userName, password);

        var result = await GetPageAsync(marker);
        var detailsResponse = await _client.GetAsync($"/api/tickets/{ticket.Id}");

        result.TotalCount.Should().Be(0);
        result.Items.Should().BeEmpty();
        detailsResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AdminVisibility_IsRestrictedToMatchingWorkspace()
    {
        var marker = $"admin-workspace-{Guid.NewGuid():N}";
        var tickets = await SeedTicketsAsync(
            new TicketSeed($"{marker}-normal", null, null),
            new TicketSeed(
                $"{marker}-demo",
                null,
                null,
                DemoExpiresAtUtc: DateTime.UtcNow.AddHours(1)));

        await LoginAsync("admin", "Admin1234");

        var normalPage = await GetPageAsync(marker);
        normalPage.Items.Select(x => x.Id).Should().Equal(tickets[0].Id);
        (await _client.GetAsync($"/api/tickets/{tickets[1].Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        SetWorkspaceToken("demo-admin", "Admin", isDemoWorkspace: true);

        var demoPage = await GetPageAsync(marker);
        demoPage.Items.Select(x => x.Id).Should().Equal(tickets[1].Id);
        (await _client.GetAsync($"/api/tickets/{tickets[0].Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AgentVisibility_PreservesAssignedAndOwnRulesWithinWorkspace()
    {
        var agentId = await GetUserIdAsync("agent");
        var otherUserId = await GetUserIdAsync("user");
        var marker = $"agent-workspace-{Guid.NewGuid():N}";
        var demoExpiration = DateTime.UtcNow.AddHours(1);
        var tickets = await SeedTicketsAsync(
            new TicketSeed($"{marker}-normal-assigned", otherUserId, agentId),
            new TicketSeed($"{marker}-normal-own", agentId, null),
            new TicketSeed($"{marker}-demo-assigned", otherUserId, agentId, DemoExpiresAtUtc: demoExpiration),
            new TicketSeed($"{marker}-demo-own", agentId, null, DemoExpiresAtUtc: demoExpiration));

        await LoginAsync("agent", "Agent123!");

        var normalPage = await GetPageAsync(marker);
        normalPage.Items.Select(x => x.Id).Should()
            .BeEquivalentTo([tickets[0].Id, tickets[1].Id]);

        SetWorkspaceToken(agentId, "Agent", isDemoWorkspace: true);

        var demoPage = await GetPageAsync(marker);
        demoPage.Items.Select(x => x.Id).Should()
            .BeEquivalentTo([tickets[2].Id, tickets[3].Id]);
    }

    [Fact]
    public async Task UserVisibility_PreservesOwnershipWithinMatchingWorkspace()
    {
        var userId = await GetUserIdAsync("user");
        var marker = $"user-workspace-{Guid.NewGuid():N}";
        var tickets = await SeedTicketsAsync(
            new TicketSeed($"{marker}-normal", userId, null),
            new TicketSeed(
                $"{marker}-demo",
                userId,
                null,
                DemoExpiresAtUtc: DateTime.UtcNow.AddHours(1)));

        await LoginAsync("user", "User123!");

        var normalPage = await GetPageAsync(marker);
        normalPage.Items.Select(x => x.Id).Should().Equal(tickets[0].Id);
        (await _client.GetAsync($"/api/tickets/{tickets[1].Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        SetWorkspaceToken(userId, "User", isDemoWorkspace: true);

        var demoPage = await GetPageAsync(marker);
        demoPage.Items.Select(x => x.Id).Should().Equal(tickets[1].Id);
        (await _client.GetAsync($"/api/tickets/{tickets[0].Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task LegacyDemoToken_UsesIsDemoClaimAsWorkspaceFallback()
    {
        var marker = $"legacy-demo-claim-{Guid.NewGuid():N}";
        var ticket = (await SeedTicketsAsync(new TicketSeed(
            marker,
            null,
            null,
            DemoExpiresAtUtc: DateTime.UtcNow.AddHours(1)))).Single();
        SetToken(
            new Claim(ClaimTypes.NameIdentifier, "legacy-demo-admin"),
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim("is_demo", "true"));

        var result = await GetPageAsync(marker);

        result.Items.Select(x => x.Id).Should().Equal(ticket.Id);
    }

    [Fact]
    public async Task Update_CannotCrossWorkspaceInEitherDirection()
    {
        var marker = $"update-workspace-{Guid.NewGuid():N}";
        var tickets = await SeedTicketsAsync(
            new TicketSeed($"{marker}-normal", null, null),
            new TicketSeed(
                $"{marker}-demo",
                null,
                null,
                DemoExpiresAtUtc: DateTime.UtcNow.AddHours(1)));

        await LoginAsync("admin", "Admin1234");
        var normalAdminResponse = await UpdateTicketAsync(tickets[1]);

        SetWorkspaceToken("demo-admin", "Admin", isDemoWorkspace: true);
        var demoAdminResponse = await UpdateTicketAsync(tickets[0]);

        normalAdminResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        demoAdminResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("user", "User123!", "User")]
    [InlineData("agent", "Agent123!", "Agent")]
    public async Task RecycleBinManagement_AsNonAdmin_ReturnsForbidden(
        string userName,
        string password,
        string role)
    {
        var actorId = await GetUserIdAsync(userName);
        var marker = $"recycle-auth-{role}-{Guid.NewGuid():N}";

        var activeTicket = (await SeedTicketsAsync(
            new TicketSeed(
                $"{marker}-active",
                role == "User" ? actorId : null,
                role == "Agent" ? actorId : null)))
            .Single();

        var deletedTicket = (await SeedTicketsAsync(
            new TicketSeed(
                $"{marker}-deleted",
                role == "User" ? actorId : null,
                role == "Agent" ? actorId : null,
                IsDeleted: true)))
            .Single();

        await LoginAsync(userName, password);

        var deletedListResponse =
            await _client.GetAsync("/api/tickets/deleted");

        var deleteResponse =
            await _client.DeleteAsync($"/api/tickets/{activeTicket.Id}");

        var restoreResponse =
            await _client.PostAsync(
                $"/api/tickets/{deletedTicket.Id}/restore",
                null);

        deletedListResponse.StatusCode.Should()
            .Be(HttpStatusCode.Forbidden);

        deleteResponse.StatusCode.Should()
            .Be(HttpStatusCode.Forbidden);

        restoreResponse.StatusCode.Should()
            .Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RecycleBinManagement_AsAdmin_IsAllowed()
    {
        var marker = $"recycle-admin-{Guid.NewGuid():N}";

        var activeTicket = (await SeedTicketsAsync(
            new TicketSeed($"{marker}-active", null, null)))
            .Single();

        var deletedTicket = (await SeedTicketsAsync(
            new TicketSeed(
                $"{marker}-deleted",
                null,
                null,
                IsDeleted: true)))
            .Single();

        await LoginAsync("admin", "Admin1234");

        var deletedListResponse =
            await _client.GetAsync("/api/tickets/deleted");

        var deleteResponse =
            await _client.DeleteAsync($"/api/tickets/{activeTicket.Id}");

        var restoreResponse =
            await _client.PostAsync(
                $"/api/tickets/{deletedTicket.Id}/restore",
                null);

        deletedListResponse.StatusCode.Should()
            .Be(HttpStatusCode.OK);

        deleteResponse.StatusCode.Should()
            .Be(HttpStatusCode.NoContent);

        restoreResponse.StatusCode.Should()
            .Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Restore_CannotCrossWorkspaceInEitherDirection()
    {
        var marker = $"restore-workspace-{Guid.NewGuid():N}";
        var tickets = await SeedTicketsAsync(
            new TicketSeed($"{marker}-normal", null, null, IsDeleted: true),
            new TicketSeed(
                $"{marker}-demo",
                null,
                null,
                IsDeleted: true,
                DemoExpiresAtUtc: DateTime.UtcNow.AddHours(1)));

        await LoginAsync("admin", "Admin1234");
        var normalAdminResponse = await _client.PostAsync(
            $"/api/tickets/{tickets[1].Id}/restore",
            null);

        SetWorkspaceToken("demo-admin", "Admin", isDemoWorkspace: true);
        var demoAdminResponse = await _client.PostAsync(
            $"/api/tickets/{tickets[0].Id}/restore",
            null);

        normalAdminResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        demoAdminResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Tickets.IgnoreQueryFilters()
                .CountAsync(x =>
                    (x.Id == tickets[0].Id || x.Id == tickets[1].Id) && x.IsDeleted))
            .Should().Be(2);
    }

    [Fact]
    public async Task CommentsAndAttachments_RejectCrossWorkspaceTicketIds()
    {
        var marker = $"resources-workspace-{Guid.NewGuid():N}";
        var tickets = await SeedTicketsAsync(
            new TicketSeed($"{marker}-normal", null, null),
            new TicketSeed(
                $"{marker}-demo",
                null,
                null,
                DemoExpiresAtUtc: DateTime.UtcNow.AddHours(1)));

        await LoginAsync("admin", "Admin1234");
        var commentsResponse = await _client.GetAsync(
            $"/api/tickets/{tickets[1].Id}/comments");

        SetWorkspaceToken("demo-admin", "Admin", isDemoWorkspace: true);
        var attachmentsResponse = await _client.GetAsync(
            $"/api/tickets/{tickets[0].Id}/attachments");

        commentsResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        attachmentsResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task LoginAsync(string userName, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            UserName = userName,
            Password = password,
            DeviceName = "TicketVisibilityTests"
        });

        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<TokenResponseDto>();
        token.Should().NotBeNull();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token!.AccessToken);
    }

    private async Task<string> GetUserIdAsync(string userName)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users
            .Where(x => x.UserName == userName)
            .Select(x => x.Id)
            .SingleAsync();
    }

    private async Task<string> GetDisplayNameAsync(string userName)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users
            .Where(x => x.UserName == userName)
            .Select(x => x.DisplayName)
            .SingleAsync();
    }

    private async Task<List<Ticket>> SeedTicketsAsync(params TicketSeed[] seeds)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var createdAtUtc = DateTime.UtcNow;
        var tickets = seeds.Select((seed, index) => new Ticket
        {
            Number = $"HDH-{Guid.NewGuid():N}",
            Title = seed.Title,
            Description = seed.Title,
            Status = seed.Status,
            Priority = seed.Priority,
            CreatedAtUtc = createdAtUtc.AddMilliseconds(index),
            RequesterUserId = seed.RequesterUserId,
            AssignedToUserId = seed.AssignedToUserId,
            IsDeleted = seed.IsDeleted,
            DeletedAtUtc = seed.IsDeleted ? createdAtUtc : null,
            DemoExpiresAtUtc = seed.DemoExpiresAtUtc,
            RowVersion = [1]
        }).ToList();

        db.Tickets.AddRange(tickets);
        await db.SaveChangesAsync();
        return tickets;
    }

    private async Task<PagedResultDto<TicketDto>> GetPageAsync(
        string search,
        string additionalQuery = "pageSize=100&pageNumber=1")
    {
        var result = await _client.GetFromJsonAsync<PagedResultDto<TicketDto>>(
            $"/api/tickets?search={Uri.EscapeDataString(search)}&{additionalQuery}");

        return result!;
    }

    private async Task<HttpResponseMessage> UpdateTicketAsync(Ticket ticket)
    {
        return await _client.PutAsJsonAsync($"/api/tickets/{ticket.Id}", new UpdateTicketDto
        {
            Title = $"{ticket.Title}-updated",
            Description = ticket.Description,
            Priority = ticket.Priority,
            Status = ticket.Status,
            RowVersionBase64 = Convert.ToBase64String(ticket.RowVersion)
        });
    }

    private void SetWorkspaceToken(
        string userId,
        string role,
        bool isDemoWorkspace)
    {
        SetToken(
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Role, role),
            new Claim(
                "is_demo_workspace",
                isDemoWorkspace ? "true" : "false"),
            new Claim("is_demo", isDemoWorkspace ? "true" : "false"));
    }

    private void SetToken(params Claim[] claims)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: JwtIssuer,
            audience: JwtAudience,
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: credentials);

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                new JwtSecurityTokenHandler().WriteToken(token));
    }

    private sealed record TicketSeed(
        string Title,
        string? RequesterUserId,
        string? AssignedToUserId,
        string Status = "New",
        string Priority = "Medium",
        bool IsDeleted = false,
        DateTime? DemoExpiresAtUtc = null);
}