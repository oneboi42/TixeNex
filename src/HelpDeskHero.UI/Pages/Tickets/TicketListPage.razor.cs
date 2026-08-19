using HelpDeskHero.Shared.Contracts.Common;
using HelpDeskHero.Shared.Contracts.Tickets;
using HelpDeskHero.UI.Services.Api;
using HelpDeskHero.UI.Services.Realtime;

namespace HelpDeskHero.UI.Pages.Tickets;

public partial class TicketListPage
{
    private TicketQueryDto _query = new();
    private PagedResultDto<TicketDto>? _result;

    private bool _loading;
    private bool _deleteBusy;
    private bool _showDeleteDialog;


    private bool _toastVisible;
    private string _toastType = "success";
    private string _toastTitle = "";
    private string? _toastMessage;
    private long _toastVersion;


    private int? _ticketIdToDelete;
    private readonly HashSet<int> _lifecycleBusyTicketIds = [];
    private string? _error;
    private string _deleteMessage = "Are you sure you want to delete this ticket?";

    protected override async Task OnInitializedAsync()
    {
        Realtime.OnTicketChanged += HandleTicketChangedAsync;
        await ReloadAsync();
        await StartRealtimeAsync();
    }

    private void ShowToast(string type, string title, string? message = null)
    {
        _toastType = type;
        _toastTitle = title;
        _toastMessage = message;
        _toastVersion++;
        _toastVisible = true;
    }
    private async Task ReloadAsync()
    {
        _loading = true;
        _error = null;

        try
        {
            _result = await TicketApi.GetPageAsync(_query);
        }
        catch
        {
            _error = "Failed to load the ticket list.";
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task StartRealtimeAsync()
    {
        try
        {
            await Realtime.StartAsync();
        }
        catch
        {
            _error ??= "Failed to connect to live updates.";
        }
    }

    private async Task HandleTicketChangedAsync(TicketLiveUpdateDto update)
    {
        await InvokeAsync(async () =>
        {
            await ReloadAsync();
            StateHasChanged();
        });
    }

    private async Task PrevAsync()
    {
        if (_query.PageNumber <= 1)
            return;

        _query.PageNumber--;
        await ReloadAsync();
    }

    private async Task NextAsync()
    {
        _query.PageNumber++;
        await ReloadAsync();
    }

    private void AskDelete(TicketDto ticket)
    {
        _ticketIdToDelete = ticket.Id;
        _deleteMessage = $"Are you sure you want to delete ticket {ticket.Number}?";
        _showDeleteDialog = true;
    }

    private void CancelDelete()
    {
        _showDeleteDialog = false;
        _ticketIdToDelete = null;
    }

    private async Task DeleteAsync()
    {
        if (_ticketIdToDelete is null)
            return;

        _deleteBusy = true;
        _error = null;

        try
        {
            var response = await TicketApi.DeleteAsync(_ticketIdToDelete.Value);

            if (!response.IsSuccessStatusCode)
            {
                _error = "Failed to delete the ticket.";
                return;
            }

            _showDeleteDialog = false;
            _ticketIdToDelete = null;

            ShowToast("success", "Ticket deleted", "The ticket was moved to the recycle bin.");

            await ReloadAsync();
        }
        catch
        {
            _error = "An error occurred while deleting the ticket.";
        }
        finally
        {
            _deleteBusy = false;
        }
    }

    private bool IsLifecycleBusy(int ticketId) => _lifecycleBusyTicketIds.Contains(ticketId);

    private async Task ChangeStatusAsync(TicketDto ticket, LifecycleAction action)
    {
        if (!_lifecycleBusyTicketIds.Add(ticket.Id))
            return;

        _error = null;

        try
        {
            var request = new TicketLifecycleRequestDto
            {
                RowVersionBase64 = ticket.RowVersionBase64
            };
            var response = action.Action switch
            {
                "start" => await TicketApi.StartAsync(ticket.Id, request),
                "resolve" => await TicketApi.ResolveAsync(ticket.Id, request),
                "close" => await TicketApi.CloseAsync(ticket.Id, request),
                "reopen" => await TicketApi.ReopenAsync(ticket.Id, request),
                _ => throw new InvalidOperationException("Unknown lifecycle action.")
            };

            if (response.IsSuccessStatusCode)
            {
                ShowToast("success", "Ticket updated", $"Ticket status changed to {action.TargetStatus}.");
                await ReloadAsync();
                return;
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                await ReloadAsync();
                _error = "This ticket was modified by another user. The latest version has been loaded.";
                return;
            }

            _error = await ApiErrorMapper.ToMessageAsync(response, "Could not update the ticket status.");
        }
        catch
        {
            _error = "Could not update the ticket status.";
        }
        finally
        {
            _lifecycleBusyTicketIds.Remove(ticket.Id);
        }
    }

    private static string GetTicketCountLabel(int count) =>
        count == 1 ? "1 ticket" : $"{count} tickets";

    private static string GetShortTicketNumber(string? number)
    {
        if (string.IsNullOrWhiteSpace(number))
            return "#—";

        var lastSeparator = number.LastIndexOf('-');
        var shortNumber = lastSeparator >= 0 && lastSeparator < number.Length - 1
            ? number[(lastSeparator + 1)..]
            : number;

        return $"#{shortNumber}";
    }

    private static string GetStatusCssClass(object? status) =>
        status?.ToString() switch
        {
            "New" => "ticket-status-new",
            "InProgress" => "ticket-status-inprogress",
            "Resolved" => "ticket-status-resolved",
            "Closed" => "ticket-status-closed",
            _ => "ticket-status-default"
        };

    private static string GetPriorityCssClass(object? priority) =>
        priority?.ToString() switch
        {
            "Low" => "ticket-priority-low",
            "Medium" => "ticket-priority-medium",
            "High" => "ticket-priority-high",
            "Critical" => "ticket-priority-critical",
            _ => "ticket-priority-default"
        };

    private static IReadOnlyList<LifecycleAction> GetLifecycleActions(TicketDto ticket)
    {
        var actions = new List<LifecycleAction>();

        if (ticket.CanStart)
            actions.Add(new("Start work", "start", "InProgress"));
        if (ticket.CanResolve)
            actions.Add(new("Resolve", "resolve", "Resolved"));
        if (ticket.CanClose)
            actions.Add(new("Close", "close", "Closed"));
        if (ticket.CanReopen)
            actions.Add(new("Reopen", "reopen", "InProgress"));

        return actions;
    }

    private sealed record LifecycleAction(string Label, string Action, string TargetStatus);


    public ValueTask DisposeAsync()
    {
        Realtime.OnTicketChanged -= HandleTicketChangedAsync;
        return ValueTask.CompletedTask;
    }
}