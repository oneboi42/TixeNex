using System.Net.Http.Headers;
using System.Net.Http.Json;
using TixeNex.Shared.Contracts.Tickets;
using Microsoft.AspNetCore.Components.Forms;

namespace TixeNex.UI.Services.Api;

public sealed class TicketAttachmentApiClient
{
    private readonly HttpClient _http;

    public TicketAttachmentApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<TicketAttachmentDto>> GetAllAsync(
        int ticketId,
        CancellationToken ct = default)
    {
        return await _http.GetFromJsonAsync<List<TicketAttachmentDto>>(
            $"api/tickets/{ticketId}/attachments",
            ct) ?? [];
    }

    public async Task<HttpResponseMessage> UploadAsync(
        int ticketId,
        IBrowserFile file,
        long maxFileSize = 10 * 1024 * 1024,
        CancellationToken ct = default)
    {
        await using var stream = file.OpenReadStream(maxFileSize, ct);

        using var content = new MultipartFormDataContent();
        using var fileContent = new StreamContent(stream);

        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(file.ContentType)
                ? "application/octet-stream"
                : file.ContentType);

        content.Add(fileContent, "file", file.Name);

        return await _http.PostAsync($"api/tickets/{ticketId}/attachments", content, ct);
    }

    public async Task<DownloadedAttachment> DownloadAsync(
        int ticketId,
        int attachmentId,
        CancellationToken ct = default)
    {
        var response = await _http.GetAsync(GetDownloadUrl(ticketId, attachmentId), ct);
        response.EnsureSuccessStatusCode();

        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
            ?? "attachment";

        var contentType = response.Content.Headers.ContentType?.MediaType
            ?? "application/octet-stream";

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);

        return new DownloadedAttachment(fileName, contentType, bytes);
    }

    public async Task<HttpResponseMessage> DeleteAsync(
        int ticketId,
        int attachmentId,
        CancellationToken ct = default)
    {
        return await _http.DeleteAsync(
            $"api/tickets/{ticketId}/attachments/{attachmentId}",
            ct);
    }

    public string GetDownloadUrl(int ticketId, int attachmentId)
    {
        var relativeUrl = $"api/tickets/{ticketId}/attachments/{attachmentId}/download";
        return new Uri(_http.BaseAddress!, relativeUrl).ToString();
    }
}

public sealed record DownloadedAttachment(string FileName, string ContentType, byte[] Content);
