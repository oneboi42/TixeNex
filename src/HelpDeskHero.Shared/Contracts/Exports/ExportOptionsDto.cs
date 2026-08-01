namespace HelpDeskHero.Shared.Contracts.Exports;

public sealed class ExportOptionsDto
{
    public IReadOnlyList<string> AllowedResourceTypes { get; set; } = [];
    public IReadOnlyList<string> AllowedFormats { get; set; } = [];
    public IReadOnlyList<string> AllowedScopes { get; set; } = [];
}
