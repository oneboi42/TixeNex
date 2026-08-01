using System.Net.Http.Json;
using HelpDeskHero.Shared.Contracts.Exports;

namespace HelpDeskHero.UI.Services.Api;

public sealed class ExportApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ExportApiClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<CreateExportResponseDto> CreateExportAsync(
        CreateExportRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("AuthorizedApi");

        using var response = await client.PostAsJsonAsync(
            requestUri: "api/exports",
            value: request,
            cancellationToken: cancellationToken);

        response.EnsureSuccessStatusCode();

        var result =
            await response.Content.ReadFromJsonAsync<CreateExportResponseDto>(
                cancellationToken: cancellationToken);

        if (result is null)
        {
            throw new InvalidOperationException(
                "The API returned an empty export creation response.");
        }

        return result;
    }

    public async Task<ExportOptionsDto> GetOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("AuthorizedApi");

        var options = await client.GetFromJsonAsync<ExportOptionsDto>(
            requestUri: "api/exports/options",
            cancellationToken: cancellationToken);

        return options ?? throw new InvalidOperationException(
            "The API returned an empty export options response.");
    }

    public async Task<List<ExportJobDto>> GetExportsAsync(
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("AuthorizedApi");

        var exports = await client.GetFromJsonAsync<List<ExportJobDto>>(
            requestUri: "api/exports",
            cancellationToken: cancellationToken);

        return exports ?? [];
    }

    public async Task<ExportDownloadResult> DownloadExportAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("AuthorizedApi");

        using var response = await client.GetAsync(
            requestUri: $"api/exports/{id}/download",
            completionOption: HttpCompletionOption.ResponseHeadersRead,
            cancellationToken: cancellationToken);

        response.EnsureSuccessStatusCode();

        var bytes = await response.Content.ReadAsByteArrayAsync(
            cancellationToken);

        var contentType =
            response.Content.Headers.ContentType?.ToString()
            ?? "text/csv";

        var fileName =
            response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName
            ?? $"export-{id}.csv";

        fileName = fileName.Trim('"');

        return new ExportDownloadResult(
            Bytes: bytes,
            FileName: fileName,
            ContentType: contentType);
    }
}

public sealed record ExportDownloadResult(
    byte[] Bytes,
    string FileName,
    string ContentType);
