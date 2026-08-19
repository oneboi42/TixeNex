using FluentAssertions;
using HelpDeskHero.Api.BackgroundJobs;
using HelpDeskHero.Api.Domain;
using HelpDeskHero.Api.Infrastructure.Notifications;
using HelpDeskHero.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskHero.Api.IntegrationTests.Notifications;

[Collection("ApiIntegration")]
public sealed class NotificationJobTests
{
    private const string TicketNumber = "HDH-20260819183419-5A630D";
    private const string DisplayTicketId = "#5A630D";
    [Fact]
    public async Task TicketCreation_NotifiesOnlyRequester()
    {
        await using var db = CreateContext();
        await SeedTicketAsync(db, requesterUserId: "requester", assignedToUserId: "assigned-agent");
        var dispatcher = new CollectingDispatcher();

        await new NotificationJob(db, dispatcher)
            .SendTicketCreatedNotificationsAsync(1);

        dispatcher.Messages.Should().ContainSingle();
        dispatcher.Messages[0].UserId.Should().Be("requester");
        dispatcher.Messages[0].Subject.Should().Be("New ticket: #5A630D");
        dispatcher.Messages[0].Body.Should().Be("Ticket #5A630D was created - Test ticket");
        dispatcher.RecipientIds.Should().NotContain(["other-user", "admin", "other-agent", "assigned-agent"]);
    }

    [Fact]
    public async Task AutomaticAssignment_NotifiesOnlyAssignedAgent()
    {
        await using var db = CreateContext();
        await SeedTicketAsync(db, requesterUserId: "requester", assignedToUserId: "assigned-agent");
        var dispatcher = new CollectingDispatcher();

        await new NotificationJob(db, dispatcher)
            .SendTicketAssignedNotificationAsync(1, "assigned-agent", false);

        dispatcher.Messages.Should().ContainSingle();
        dispatcher.Messages[0].UserId.Should().Be("assigned-agent");
        dispatcher.Messages[0].Subject.Should().Be("Ticket assigned: #5A630D");
        dispatcher.RecipientIds.Should().NotContain(["requester", "other-agent"]);
    }

    [Fact]
    public async Task TicketCreation_WhenRequesterIsAssignee_SendsOneCombinedNotification()
    {
        await using var db = CreateContext();
        await SeedTicketAsync(db, requesterUserId: "requester", assignedToUserId: "requester");
        var dispatcher = new CollectingDispatcher();

        await new NotificationJob(db, dispatcher)
            .SendTicketCreatedNotificationsAsync(1);

        dispatcher.Messages.Should().ContainSingle();
        dispatcher.Messages[0].UserId.Should().Be("requester");
        dispatcher.Messages[0].Subject.Should().Be("Ticket created and assigned: #5A630D");
        dispatcher.Messages[0].Body.Should().Be(
            "Ticket #5A630D - Test ticket was created and assigned to you.");
    }

    [Fact]
    public async Task TicketCreation_MissingTicket_DoesNotNotify()
    {
        await using var db = CreateContext();
        var dispatcher = new CollectingDispatcher();

        await new NotificationJob(db, dispatcher)
            .SendTicketCreatedNotificationsAsync(404);

        dispatcher.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task TicketCreation_DeletedTicket_DoesNotNotify()
    {
        await using var db = CreateContext();
        await SeedTicketAsync(db, requesterUserId: "requester", isDeleted: true);
        var dispatcher = new CollectingDispatcher();

        await new NotificationJob(db, dispatcher)
            .SendTicketCreatedNotificationsAsync(1);

        dispatcher.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task UserReply_NotifiesAssignedAgentAndPreviousAdminParticipant()
    {
        await using var db = CreateContext();
        await SeedTicketConversationAsync(db);
        var dispatcher = new CollectingDispatcher();

        await new NotificationJob(db, dispatcher)
            .SendTicketCommentNotificationsAsync(1, "user");

        dispatcher.RecipientIds.Should().BeEquivalentTo(["agent", "admin"]);
        dispatcher.Messages.Should().OnlyContain(x =>
            x.Subject == $"New reply on ticket {DisplayTicketId}" &&
            x.Body == $"A new reply was added to ticket {DisplayTicketId} - Test ticket");
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
    [InlineData(false, "Ticket assigned: #5A630D")]
    [InlineData(true, "Ticket reassigned: #5A630D")]
    public async Task Assignment_NotifiesOnlyNewAssignee(bool isReassignment, string expectedSubject)
    {
        await using var db = CreateContext();
        await SeedTicketConversationAsync(db);
        var dispatcher = new CollectingDispatcher();

        await new NotificationJob(db, dispatcher)
            .SendTicketAssignedNotificationAsync(1, "agent", isReassignment);

        dispatcher.Messages.Should().ContainSingle();
        dispatcher.Messages[0].UserId.Should().Be("agent");
        dispatcher.RecipientIds.Should().NotContain("previous-agent");
        dispatcher.Messages[0].Subject.Should().Be(expectedSubject);
        dispatcher.Messages[0].Body.Should().Be("Ticket #5A630D - Test ticket has been assigned to you.");
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
            Number = TicketNumber,
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

    private static async Task SeedTicketAsync(
        AppDbContext db,
        string requesterUserId,
        string? assignedToUserId = null,
        bool isDeleted = false)
    {
        db.Users.AddRange(
            new ApplicationUser { Id = "requester", UserName = "requester", DisplayName = "Requester", IsActive = true },
            new ApplicationUser { Id = "other-user", UserName = "other-user", DisplayName = "Other User", IsActive = true },
            new ApplicationUser { Id = "admin", UserName = "admin", DisplayName = "Admin", IsActive = true },
            new ApplicationUser { Id = "assigned-agent", UserName = "assigned-agent", DisplayName = "Assigned Agent", IsActive = true },
            new ApplicationUser { Id = "other-agent", UserName = "other-agent", DisplayName = "Other Agent", IsActive = true });

        db.Tickets.Add(new Ticket
        {
            Id = 1,
            Number = TicketNumber,
            Title = "Test ticket",
            Description = "Test",
            RequesterUserId = requesterUserId,
            AssignedToUserId = assignedToUserId,
            IsDeleted = isDeleted
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