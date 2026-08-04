using System.Security.Claims;
using FluentAssertions;
using HelpDeskHero.Api.Controllers;
using HelpDeskHero.Api.Domain;
using HelpDeskHero.Api.Infrastructure.Persistence;
using HelpDeskHero.Shared.Contracts.Notifications;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskHero.Api.IntegrationTests.Notifications;

public sealed class NotificationsControllerTests
{
    [Fact]
    public async Task GetMine_ReturnsOnlyCurrentUsersNotifications()
    {
        await using var db = CreateContext();
        db.UserNotifications.AddRange(
            Notification("user-a", "A"),
            Notification("user-b", "B"));
        await db.SaveChangesAsync();
        var controller = CreateController(db, "user-b");

        var result = await controller.GetMine(default);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var notifications = ok.Value.Should()
            .BeAssignableTo<IReadOnlyList<UserNotificationDto>>().Subject;
        notifications.Should().ContainSingle(x => x.Subject == "B");
        notifications.Should().NotContain(x => x.Subject == "A");
    }

    [Fact]
    public async Task MarkAsRead_CannotModifyAnotherUsersNotification()
    {
        await using var db = CreateContext();
        var userANotification = Notification("user-a", "A");
        db.UserNotifications.Add(userANotification);
        await db.SaveChangesAsync();
        var controller = CreateController(db, "user-b");

        var result = await controller.MarkAsRead(userANotification.Id, default);

        result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        (await db.UserNotifications.SingleAsync()).IsRead.Should().BeFalse();
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"notifications-controller-{Guid.NewGuid():N}")
            .Options;

        return new AppDbContext(options);
    }

    private static NotificationsController CreateController(AppDbContext db, string userId)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId)],
            "test");

        return new NotificationsController(db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(identity)
                }
            }
        };
    }

    private static UserNotification Notification(string userId, string subject) => new()
    {
        UserId = userId,
        Subject = subject,
        Body = subject,
        IsRead = false,
        CreatedAtUtc = DateTime.UtcNow
    };
}
