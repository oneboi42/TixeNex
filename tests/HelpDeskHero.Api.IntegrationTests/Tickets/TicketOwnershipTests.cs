using System.Security.Claims;
using FluentAssertions;
using HelpDeskHero.Api.Application.Interfaces;
using HelpDeskHero.Api.Application.Services;
using HelpDeskHero.Api.Application.TicketVisibility;
using HelpDeskHero.Api.Controllers;
using HelpDeskHero.Api.Domain;
using HelpDeskHero.Api.Infrastructure.Persistence;
using HelpDeskHero.Api.Infrastructure.Services;
using HelpDeskHero.Shared.Contracts.Tickets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

namespace HelpDeskHero.Api.IntegrationTests.Tickets;

public sealed class TicketOwnershipTests
{
    [Fact]
    public async Task CreateTicket_WithoutNameIdentifier_ReturnsUnauthorizedAndDoesNotSave()
    {
        await using var db = CreateContext();
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Role, "User")],
                "TestAuth"))
        };
        var controller = CreateController(db, httpContext);

        var result = await controller.Create(new CreateTicketDto
        {
            Title = "Missing requester claim",
            Description = "Ticket must not be persisted",
            Priority = "Medium"
        }, default);

        result.Result.Should().BeOfType<UnauthorizedResult>();
        (await db.Tickets.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task LegacyTicket_WithNullRequester_CanBeSavedAndRead()
    {
        await using var db = CreateContext();
        db.Tickets.Add(new Ticket
        {
            Number = "HDH-LEGACY",
            Title = "Legacy ticket",
            Description = "Ticket created before requester ownership",
            Priority = "Medium",
            Status = "New",
            CreatedAtUtc = DateTime.UtcNow,
            RequesterUserId = null
        });

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var ticket = await db.Tickets.SingleAsync();
        ticket.RequesterUserId.Should().BeNull();
    }

    private static TicketsController CreateController(AppDbContext db, DefaultHttpContext httpContext)
    {
        return new TicketsController(
            db,
            new AuditService(db, new HttpContextAccessor { HttpContext = httpContext }),
            new SlaCalculator(db),
            new NoopTicketAssignmentService(),
            null!, // UserManager is not used by these tests.
            new TicketVisibilityContextResolver(),
            new OutboxWriter(db),
            new TestWebHostEnvironment())
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"ticket-ownership-{Guid.NewGuid():N}")
            .Options;

        return new AppDbContext(options);
    }

    private sealed class NoopTicketAssignmentService : ITicketAssignmentService
    {
        public Task<string?> AssignAsync(Ticket ticket, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "HelpDeskHero.Tests";
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
