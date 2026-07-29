namespace HelpDeskHero.Shared.Contracts.Exports;

public sealed class CreateExportResponseDto
{
    public Guid Id { get; set; }

    public string Status { get; set; } = string.Empty;
}