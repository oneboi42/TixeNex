namespace TixeNex.Api.BackgroundJobs.Contracts;

public interface INotificationJob
{
    Task SendTicketCreatedNotificationsAsync(int ticketId, CancellationToken ct = default);
    Task SendTicketAssignedNotificationAsync(
        int ticketId,
        string assignedToUserId,
        bool isReassignment,
        CancellationToken ct = default);
    Task SendTicketDeletedNotificationsAsync(
        int ticketId,
        string deletedByUserId,
        CancellationToken ct = default);
    Task SendTicketLifecycleNotificationsAsync(
        int ticketId,
        string action,
        string actingUserId,
        CancellationToken ct = default);
    Task SendTicketCommentNotificationsAsync(int ticketId, string authorUserId, CancellationToken ct = default);
    Task SendDailySummaryAsync(CancellationToken ct = default);
}
