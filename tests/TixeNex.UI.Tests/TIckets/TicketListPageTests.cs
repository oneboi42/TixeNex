using System.Net;
using System.Security.Claims;
using Bunit;
using FluentAssertions;
using TixeNex.Shared.Contracts.Common;
using TixeNex.Shared.Contracts.Tickets;
using TixeNex.UI.Pages.Tickets;
using TixeNex.UI.Services.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using TixeNex.UI.Services.Realtime;

namespace TixeNex.UI.Tests.Tickets;

public sealed class TicketListPageTests : BunitContext
{
    [Fact]
    public void TicketListPage_ShouldRenderTicketTitle()
    {
        var cut = RenderPage(new FakeTicketApiClient());

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Test Ticket from bUnit");
        });
    }

    [Fact]
    public void TicketListPage_ActionsUseStandardOutlinedButtonsAndEditIsNotRendered()
    {
        var cut = RenderPage(new FakeTicketApiClient(new TicketDto
        {
            Id = 9,
            Number = "HDH-0009",
            Title = "Editable ticket",
            Status = "New",
            Priority = "Medium",
            CanEdit = true,
            CreatedAtUtc = DateTime.UtcNow
        }));

        cut.WaitForAssertion(() => cut.FindAll("button")
            .Should().ContainSingle(button => button.TextContent.Trim() == "View"));

        var viewButton = cut.FindAll("button")
            .Single(button => button.TextContent.Trim() == "View");
        var deleteButton = cut.FindAll("button")
            .Single(button => button.TextContent.Trim() == "Delete");
        viewButton.ClassList.Should().Contain("btn");
        viewButton.ClassList.Should().Contain("btn-outline-primary");
        deleteButton.ClassList.Should().Contain("btn");
        deleteButton.ClassList.Should().Contain("btn-outline-danger");
        deleteButton.ClassList.Should().NotContain("btn-sm");
        deleteButton.ParentElement.Should().BeSameAs(viewButton.ParentElement);
        deleteButton.ParentElement!.ClassList.Should().Contain("align-items-center");
        cut.FindAll("button").Should().NotContain(button => button.TextContent.Trim() == "Edit");

        viewButton.Click();

        Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith("/tickets/9");
    }

    [Fact]
    public void TicketListPage_AsUser_ShouldRenderCreateAndExportHistoryLinks()
    {
        var cut = RenderPage(new FakeTicketApiClient(), "User");

        cut.WaitForAssertion(() =>
        {
            cut.Find("a[href='tickets/create']").TextContent.Should().Contain("New ticket");
            cut.Find("a[href='exports']").TextContent.Should().Contain("Export history");
        });
    }

    [Fact]
    public void TicketListPage_ShouldRenderAssignedAgentDisplayName()
    {
        var cut = RenderPage(new FakeTicketApiClient(new TicketDto
        {
            Id = 4,
            Number = "HDH-0004",
            Title = "Assigned ticket",
            AssignedToUserId = "agent-id",
            AssignedToDisplayName = "Support Agent",
            CreatedAtUtc = DateTime.UtcNow
        }));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Support Agent"));
    }

    [Fact]
    public void TicketListPage_ShouldRenderUnassignedWhenTicketHasNoAgent()
    {
        var cut = RenderPage(new FakeTicketApiClient(new TicketDto
        {
            Id = 5,
            Number = "HDH-0005",
            Title = "Unassigned ticket",
            CreatedAtUtc = DateTime.UtcNow
        }));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Unassigned"));
    }

    [Fact]
    public void TicketListPage_AsAdmin_ShowsRequesterColumnAndDisplayName()
    {
        var cut = RenderPage(new FakeTicketApiClient(new TicketDto
        {
            Id = 6,
            Number = "HDH-0006",
            Title = "Requester ticket",
            RequesterDisplayName = "Requesting User",
            CreatedAtUtc = DateTime.UtcNow
        }));

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("th").Should().Contain(x => x.TextContent.Trim() == "Requester");
            cut.Markup.Should().Contain("Requesting User");
        });
    }

    [Fact]
    public void TicketListPage_AsAdmin_ShowsUnknownForLegacyRequester()
    {
        var cut = RenderPage(new FakeTicketApiClient(new TicketDto
        {
            Id = 7,
            Number = "HDH-0007",
            Title = "Legacy ticket",
            CreatedAtUtc = DateTime.UtcNow
        }));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Unknown"));
    }

    [Theory]
    [InlineData("Agent")]
    [InlineData("User")]
    public void TicketListPage_AsNonAdmin_HidesRequesterColumnAndValue(string role)
    {
        var cut = RenderPage(new FakeTicketApiClient(new TicketDto
        {
            Id = 8,
            Number = "HDH-0008",
            Title = "Hidden requester ticket",
            RequesterDisplayName = "Sensitive Requester",
            CreatedAtUtc = DateTime.UtcNow
        }), role);

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Hidden requester ticket"));
        cut.FindAll("th").Should().NotContain(x => x.TextContent.Trim() == "Requester");
        cut.Markup.Should().NotContain("Sensitive Requester");
    }

    [Fact]
    public void TicketListPage_StartCapabilityCallsDedicatedStartEndpoint()
    {
        var api = new FakeTicketApiClient(new TicketDto
        {
            Id = 2,
            Number = "HDH-0002",
            Title = "Startable ticket",
            Description = "desc",
            Status = "New",
            Priority = "Medium",
            CanStart = true,
            RowVersionBase64 = Convert.ToBase64String([1])
        });
        var cut = RenderPage(api);

        cut.WaitForAssertion(() =>
            cut.FindAll("button").Single(x => x.TextContent.Contains("Start work")).Click());

        cut.WaitForAssertion(() => api.StartCalls.Should().Be(1));
        api.UpdateCalls.Should().Be(0);
    }

    [Fact]
    public void TicketListPage_ReopenButtonFollowsCapability()
    {
        var api = new FakeTicketApiClient(new TicketDto
        {
            Id = 3,
            Number = "HDH-0003",
            Title = "Reopenable ticket",
            Description = "desc",
            Status = "Resolved",
            Priority = "Medium",
            CanReopen = true,
            RowVersionBase64 = Convert.ToBase64String([1])
        });
        var cut = RenderPage(api);

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Reopen"));
        cut.FindAll("button").Single(x => x.TextContent.Contains("Reopen")).Click();
        cut.WaitForAssertion(() => api.ReopenCalls.Should().Be(1));
    }

    private IRenderedComponent<Bunit.Rendering.ContainerFragment> RenderPage(
        FakeTicketApiClient api,
        string role = "Admin")
    {
        Services.AddAuthorizationCore();
        Services.AddSingleton<IAuthorizationService, RoleAwareAuthorizationService>();
        Services.AddSingleton<AuthenticationStateProvider>(
            new FakeAuthenticationStateProvider(role));
        Services.AddSingleton<ITicketApiClient>(api);
        Services.AddSingleton<ITicketsRealtimeClient, FakeTicketsRealtimeClient>();

        return Render(builder =>
        {
            builder.OpenComponent<CascadingAuthenticationState>(0);
            builder.AddAttribute(1, "ChildContent", (RenderFragment)(childBuilder =>
            {
                childBuilder.OpenComponent<TicketListPage>(2);
                childBuilder.CloseComponent();
            }));
            builder.CloseComponent();
        });
    }

    private sealed class RoleAwareAuthorizationService : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            IEnumerable<IAuthorizationRequirement> requirements)
        {
            var authorized = requirements.All(requirement => requirement switch
            {
                RolesAuthorizationRequirement roles =>
                    roles.AllowedRoles.Any(user.IsInRole),
                DenyAnonymousAuthorizationRequirement =>
                    user.Identity?.IsAuthenticated == true,
                _ => true
            });

            return Task.FromResult(authorized
                ? AuthorizationResult.Success()
                : AuthorizationResult.Failed());
        }

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            string policyName) =>
            Task.FromResult(AuthorizationResult.Success());
    }

    private sealed class FakeAuthenticationStateProvider : AuthenticationStateProvider
    {
        private readonly string _role;

        public FakeAuthenticationStateProvider(string role)
        {
            _role = role;
        }

        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.Name, _role.ToLowerInvariant()),
                new Claim(ClaimTypes.Role, _role)
            };

            var identity = new ClaimsIdentity(claims, "TestAuth");
            var user = new ClaimsPrincipal(identity);

            return Task.FromResult(new AuthenticationState(user));
        }
    }

    private sealed class FakeTicketApiClient : ITicketApiClient
    {
        private readonly TicketDto _ticket;

        public FakeTicketApiClient(TicketDto? ticket = null)
        {
            _ticket = ticket ?? new TicketDto
            {
                Id = 1,
                Number = "HDH-0001",
                Title = "Test Ticket from bUnit",
                Description = "desc",
                Status = "New",
                Priority = "High",
                CreatedAtUtc = DateTime.UtcNow
            };
        }

        public int UpdateCalls { get; private set; }
        public int StartCalls { get; private set; }
        public int ReopenCalls { get; private set; }

        public Task<PagedResultDto<TicketDto>?> GetPageAsync(
            TicketQueryDto query,
            CancellationToken ct = default)
        {
            var result = new PagedResultDto<TicketDto>
            {
                PageNumber = 1,
                PageSize = 10,
                TotalCount = 1,
                Items = [_ticket]
            };

            return Task.FromResult<PagedResultDto<TicketDto>?>(result);
        }

        public Task<TicketDto?> GetByIdAsync(
            int id,
            CancellationToken ct = default)
        {
            return Task.FromResult<TicketDto?>(null);
        }

        public Task<HttpResponseMessage> CreateAsync(
            CreateTicketDto dto,
            CancellationToken ct = default)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));
        }

        public Task<HttpResponseMessage> UpdateAsync(
            int id,
            UpdateTicketDto dto,
            CancellationToken ct = default)
        {
            UpdateCalls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }

        public Task<HttpResponseMessage> AssignAsync(
            int id,
            AssignTicketDto dto,
            CancellationToken ct = default)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }

        public Task<HttpResponseMessage> StartAsync(
            int id,
            TicketLifecycleRequestDto dto,
            CancellationToken ct = default)
        {
            StartCalls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }

        public Task<HttpResponseMessage> ResolveAsync(
            int id,
            TicketLifecycleRequestDto dto,
            CancellationToken ct = default) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));

        public Task<HttpResponseMessage> CloseAsync(
            int id,
            TicketLifecycleRequestDto dto,
            CancellationToken ct = default) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));

        public Task<HttpResponseMessage> ReopenAsync(
            int id,
            TicketLifecycleRequestDto dto,
            CancellationToken ct = default)
        {
            ReopenCalls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }

        public Task<HttpResponseMessage> DeleteAsync(
            int id,
            CancellationToken ct = default)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }


            public Task<HttpResponseMessage> ExportCsvAsync(
            TicketQueryDto query,
            CancellationToken ct = default)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }

        public Task<IReadOnlyList<TicketDto>> GetDeletedAsync(
            CancellationToken ct = default)
        {
            IReadOnlyList<TicketDto> result = [];

            return Task.FromResult(result);
        }

        public Task<HttpResponseMessage> RestoreAsync(
            int id,
            CancellationToken ct = default)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }

    private sealed class FakeTicketsRealtimeClient : ITicketsRealtimeClient
    {
        public event Func<TicketLiveUpdateDto, Task>? OnTicketChanged;

        public Task StartAsync(CancellationToken ct = default)
        {
            _ = OnTicketChanged;

            return Task.CompletedTask;
        }
    }
}
