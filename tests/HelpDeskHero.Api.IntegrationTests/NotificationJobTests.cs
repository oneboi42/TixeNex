using FluentAssertions;
using HelpDeskHero.Api.BackgroundJobs;
using HelpDeskHero.Api.Domain;
using HelpDeskHero.Api.Infrastructure.Notifications;
using HelpDeskHero.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskHero.Api.IntegrationTests;

[Collection("ApiIntegration")]
public sealed class NotificationJobTests
{
    [Fact]
    public async Task UserReply_NotifiesAssignedAgentAndPreviousAdminParticipant()
    {
        await using var db = CreateContext();
        await SeedTicketConversationAsync(db);
        var dispatcher = new CollectingDispatcher();

        await new NotificationJob(db, dispatcher)
            .SendTicketCommentNotificationsAsync(1, "user");

        dispatcher.RecipientIds.Should().BeEquivalentTo(["agent", "admin"]);
    }

    [Fact]
    public async Task AgentReply_NotifiesTicketCreatorAndPreviousAdminParticipant()
    {
        await using var db = CreateContext();
        await SeedTicketConversationAsync(db);
        var dispatcher = new CollectingDispatcher();

        await new NotificationJob(db, dispatcher)
            .SendTicketCommentNotificationsAsync(1, "agent");

        dispatcher.RecipientIds.Should().BeEquivalentTo(["user", "admin"]);
    }

    [Fact]
    public async Task AdminReply_NotifiesTicketCreatorAndAssignedAgent()
    {
        await using var db = CreateContext();
        await SeedTicketConversationAsync(db);
        var dispatcher = new CollectingDispatcher();

        await new NotificationJob(db, dispatcher)
            .SendTicketCommentNotificationsAsync(1, "admin");

        dispatcher.RecipientIds.Should().BeEquivalentTo(["user", "agent"]);
    }

    [Theory]
    [InlineData(false, "Ticket assigned: HDH-1")]
    [InlineData(true, "Ticket reassigned: HDH-1")]
    public async Task Assignment_NotifiesOnlyNewAssignee(bool isReassignment, string expectedSubject)
    {
        await using var db = CreateContext();
        await SeedTicketConversationAsync(db);
        var dispatcher = new CollectingDispatcher();

        await new NotificationJob(db, dispatcher)
            .SendTicketAssignedNotificationAsync(1, "agent", isReassignment);

        dispatcher.Messages.Should().ContainSingle();
        dispatcher.Messages[0].UserId.Should().Be("agent");
        dispatcher.Messages[0].Subject.Should().Be(expectedSubject);
        dispatcher.Messages[0].Body.Should().Be("Ticket HDH-1 - Test ticket has been assigned to you.");
    }

    [Fact]
    public async Task Assignment_MissingTicket_DoesNotNotify()
    {
        await using var db = CreateContext();
        var dispatcher = new CollectingDispatcher();

        await new NotificationJob(db, dispatcher)
            .SendTicketAssignedNotificationAsync(404, "agent", false);

        dispatcher.Messages.Should().BeEmpty();
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"notification-job-{Guid.NewGuid():N}")
            .Options;

        return new AppDbContext(options);
    }

    private static async Task SeedTicketConversationAsync(AppDbContext db)
    {
        db.Users.AddRange(
            new ApplicationUser { Id = "user", UserName = "user", DisplayName = "User", IsActive = true },
            new ApplicationUser { Id = "agent", UserName = "agent", DisplayName = "Agent", IsActive = true },
            new ApplicationUser { Id = "admin", UserName = "admin", DisplayName = "Admin", IsActive = true });

        db.Tickets.Add(new Ticket
        {
            Id = 1,
            Number = "HDH-1",
            Title = "Test ticket",
            Description = "Test",
            AssignedToUserId = "agent"
        });

        db.AuditLogs.Add(new AuditLog
        {
            Action = "Create",
            EntityName = "Ticket",
            EntityId = "1",
            UserId = "user"
        });

        db.TicketComments.Add(new TicketComment
        {
            TicketId = 1,
            Body = "Previous admin reply",
            CreatedByUserId = "admin",
            CreatedByDisplayName = "Admin"
        });

        await db.SaveChangesAsync();
    }

    private sealed class CollectingDispatcher : INotificationDispatcher
    {
        public List<string> RecipientIds { get; } = [];
        public List<NotificationMessage> Messages { get; } = [];

        public Task DispatchAsync(NotificationMessage message, CancellationToken ct = default)
        {
            Messages.Add(message);
            if (message.UserId is not null)
                RecipientIds.Add(message.UserId);

            return Task.CompletedTask;
        }
    }
}
