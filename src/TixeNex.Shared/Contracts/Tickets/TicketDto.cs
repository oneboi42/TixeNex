namespace TixeNex.Shared.Contracts.Tickets;

public sealed class TicketDto
{
    public int Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = "New";
    public string Priority { get; set; } = "Medium";

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    public string? RequesterDisplayName { get; set; }

    public bool IsRequesterCurrentUser { get; set; }

    public bool IsDemoTicket { get; set; }

    public DateTime? DemoExpiresAtUtc { get; set; }

    public string? AssignedToUserId { get; set; }
    public string? AssignedToDisplayName { get; set; }

    public bool CanEdit { get; set; }
    public bool CanStart { get; set; }
    public bool CanResolve { get; set; }
    public bool CanClose { get; set; }
    public bool CanReopen { get; set; }

    public string RowVersionBase64 { get; set; } = string.Empty;
}