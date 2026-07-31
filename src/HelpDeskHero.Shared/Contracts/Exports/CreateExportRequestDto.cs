namespace HelpDeskHero.Shared.Contracts.Exports;

public sealed class CreateExportRequestDto
{
    public string ResourceType { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
}
