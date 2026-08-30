using FluentAssertions;
using TixeNex.Api.Domain;
using TixeNex.Worker.Services;

namespace TixeNex.Worker.Tests.Exports;

public sealed class ExportWorkspaceFilteringTests
{
    [Fact]
    public void ApplyTicketFilters_DemoWorkspace_IncludesDemoTicketsAndExcludesNormalTickets()
    {
        var tickets = new[]
        {
            Ticket(1, TicketOrigin.DemoSeed),
            Ticket(2, TicketOrigin.DemoUser),
            Ticket(3, TicketOrigin.Normal)
        }.AsQueryable();

        var job = new ExportJob
        {
            UserId = "demo-admin",
            Scope = ExportScope.All
        };

        var result = ExportService.ApplyTicketFilters(
                tickets,
                job,
                isDemoWorkspace: true)
            .Select(ticket => ticket.Id)
            .ToList();

        result.Should().BeEquivalentTo([1, 2]);
    }

    [Fact]
    public void ApplyTicketFilters_NormalWorkspace_IncludesNormalTicketsAndExcludesDemoTickets()
    {
        var tickets = new[]
        {
            Ticket(1, TicketOrigin.DemoSeed),
            Ticket(2, TicketOrigin.DemoUser),
            Ticket(3, TicketOrigin.Normal)
        }.AsQueryable();

        var job = new ExportJob
        {
            UserId = "admin",
            Scope = ExportScope.All
        };

        var result = ExportService.ApplyTicketFilters(
                tickets,
                job,
                isDemoWorkspace: false)
            .Select(ticket => ticket.Id)
            .ToList();

        result.Should().Equal(3);
    }

    [Fact]
    public void ApplyTicketFilters_DemoWorkspace_ExcludesDeletedDemoTickets()
    {
        var tickets = new[]
        {
            Ticket(1, TicketOrigin.DemoSeed),
            Ticket(2, TicketOrigin.DemoSeed, isDeleted: true)
        }.AsQueryable();

        var job = new ExportJob
        {
            UserId = "demo-admin",
            Scope = ExportScope.All
        };

        var result = ExportService.ApplyTicketFilters(
                tickets,
                job,
                isDemoWorkspace: true)
            .Select(ticket => ticket.Id)
            .ToList();

        result.Should().Equal(1);
    }

    private static Ticket Ticket(
        int id,
        TicketOrigin origin,
        bool isDeleted = false) =>
        new()
        {
            Id = id,
            Number = $"HD-{id:0000}",
            Title = $"Ticket {id}",
            Origin = origin,
            IsDeleted = isDeleted
        };
}