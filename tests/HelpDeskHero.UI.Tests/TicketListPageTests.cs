using System.Net;
using System.Security.Claims;
using Bunit;
using FluentAssertions;
using HelpDeskHero.Shared.Contracts.Common;
using HelpDeskHero.Shared.Contracts.Tickets;
using HelpDeskHero.UI.Pages.Tickets;
using HelpDeskHero.UI.Services.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using HelpDeskHero.UI.Services.Realtime;

namespace HelpDeskHero.UI.Tests;

public sealed class TicketListPageTests : BunitContext
{
    [Fact]
    public void TicketListPage_ShouldRenderTicketTitle()
    {
        Services.AddAuthorizationCore();
        Services.AddSingleton<IAuthorizationService, AlwaysAllowAuthorizationService>();

        Services.AddSingleton<AuthenticationStateProvider>(
            new FakeAuthenticationStateProvider());

        Services.AddSingleton<ITicketApiClient>(new FakeTicketApiClient());
        
        Services.AddSingleton<ITicketsRealtimeClient, FakeTicketsRealtimeClient>();


        var cut = Render(builder =>
        {
            builder.OpenComponent<CascadingAuthenticationState>(0);

            builder.AddAttribute(1, "ChildContent", (RenderFragment)(childBuilder =>
            {
                childBuilder.OpenComponent<TicketListPage>(2);
                childBuilder.CloseComponent();
            }));

            builder.CloseComponent();
        });

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Test Ticket from bUnit");
        });
    }

    private sealed class AlwaysAllowAuthorizationService : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            IEnumerable<IAuthorizationRequirement> requirements)
        {
            return Task.FromResult(AuthorizationResult.Success());
        }

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            string policyName)
        {
            return Task.FromResult(AuthorizationResult.Success());
        }
    }

    private sealed class FakeAuthenticationStateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.Name, "admin"),
                new Claim(ClaimTypes.Role, "Admin")
            };

            var identity = new ClaimsIdentity(claims, "TestAuth");
            var user = new ClaimsPrincipal(identity);

            return Task.FromResult(new AuthenticationState(user));
        }
    }

    private sealed class FakeTicketApiClient : ITicketApiClient
    {
        public Task<PagedResultDto<TicketDto>?> GetPageAsync(
            TicketQueryDto query,
            CancellationToken ct = default)
        {
            var result = new PagedResultDto<TicketDto>
            {
                PageNumber = 1,
                PageSize = 10,
                TotalCount = 1,
                Items =
                [
                    new TicketDto
                    {
                        Id = 1,
                        Number = "HDH-0001",
                        Title = "Test Ticket from bUnit",
                        Description = "desc",
                        Status = "New",
                        Priority = "High",
                        CreatedAtUtc = DateTime.UtcNow
                    }
                ]
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
