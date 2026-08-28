namespace TixeNex.Api.Application.Services.Exports;

public sealed record ExportRequested(
    Guid ExportJobId,
    string UserId);