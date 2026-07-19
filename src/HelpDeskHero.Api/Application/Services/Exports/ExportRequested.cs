namespace HelpDeskHero.Api.Application.Services.Exports;

public sealed record ExportRequested(
    Guid ExportJobId,
    string UserId);