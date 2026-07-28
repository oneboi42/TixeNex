using FluentAssertions;
using HelpDeskHero.Api.Application.Interfaces;
using HelpDeskHero.Api.Application.Services;
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

namespace HelpDeskHero.Api.IntegrationTests;

[Collection("ApiIntegration")]
public sealed class TicketLifecycleTests
{
    [Fact]
    public async Task NewTicket_CanStartWork()
    {
        await using var db = CreateContext();
        var ticket = await AddTicketAsync(db, "New");

        var result = await UpdateStatusAsync(db, ticket, "InProgress");

        result.Should().BeOfType<NoContentResult>();
        (await db.Tickets.SingleAsync()).Status.Should().Be("InProgress");
    }

    [Fact]
    public async Task InProgressTicket_ResolvingSetsResolvedAtUtc()
    {
        await using var db = CreateContext();
        var ticket = await AddTicketAsync(db, "InProgress");

        await UpdateStatusAsync(db, ticket, "Resolved");

        var updated = await db.Tickets.SingleAsync();
        updated.Status.Should().Be("Resolved");
        updated.ResolvedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task ResolvedTicket_ClosingPreservesResolvedAtUtc()
    {
        await using var db = CreateContext();
        var resolvedAtUtc = DateTime.UtcNow.AddMinutes(-5);
        var ticket = await AddTicketAsync(db, "Resolved", resolvedAtUtc);

        await UpdateStatusAsync(db, ticket, "Closed");

        var updated = await db.Tickets.SingleAsync();
        updated.Status.Should().Be("Closed");
        updated.ResolvedAtUtc.Should().Be(resolvedAtUtc);
    }

    [Fact]
    public async Task ResolvedTicket_ReopeningClearsResolvedAtUtc()
    {
        await using var db = CreateContext();
        var ticket = await AddTicketAsync(db, "Resolved", DateTime.UtcNow.AddMinutes(-5));

        await UpdateStatusAsync(db, ticket, "InProgress");

        var updated = await db.Tickets.SingleAsync();
        updated.Status.Should().Be("InProgress");
        updated.ResolvedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task ResolveSlaMonitor_DoesNotEscalateResolvedTickets()
    {
        await using var db = CreateContext();
        await AddTicketAsync(db, "New", dueResolveAtUtc: DateTime.UtcNow.AddMinutes(-1));
        await AddTicketAsync(db, "Resolved", dueResolveAtUtc: DateTime.UtcNow.AddMinutes(-1));

        var monitor = new SlaMonitorService(db, new OutboxWriter(db));
        await monitor.CheckBreachesAsync();

        var tickets = await db.Tickets.OrderBy(x => x.Id).ToListAsync();
        tickets[0].EscalationLevel.Should().Be(1);
        tickets[1].EscalationLevel.Should().Be(0);
        (await db.OutboxMessages.CountAsync()).Should().Be(1);
    }

    private static async Task<IActionResult> UpdateStatusAsync(AppDbContext db, Ticket ticket, string status)
    {
        var httpContext = new DefaultHttpContext();
        var controller = new TicketsController(
            db,
            new AuditService(db, new HttpContextAccessor { HttpContext = httpContext }),
            new SlaCalculator(db),
            new NoopTicketAssignmentService(),
            new OutboxWriter(db),
            new TestWebHostEnvironment())
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        return await controller.Update(ticket.Id, new UpdateTicketDto
        {
            Title = ticket.Title,
            Description = ticket.Description,
            Priority = ticket.Priority,
            Status = status,
            RowVersionBase64 = Convert.ToBase64String(ticket.RowVersion)
        }, default);
    }

    private static async Task<Ticket> AddTicketAsync(
        AppDbContext db,
        string status,
        DateTime? resolvedAtUtc = null,
        DateTime? dueResolveAtUtc = null)
    {
        var ticket = new Ticket
        {
            Number = $"HDH-{Guid.NewGuid():N}",
            Title = "Lifecycle test ticket",
            Description = "Lifecycle test ticket description",
            Status = status,
            Priority = "Medium",
            CreatedAtUtc = DateTime.UtcNow,
            ResolvedAtUtc = resolvedAtUtc,
            DueResolveAtUtc = dueResolveAtUtc,
            RowVersion = [1]
        };

        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();
        return ticket;
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"ticket-lifecycle-{Guid.NewGuid():N}")
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
