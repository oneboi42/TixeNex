namespace HelpDeskHero.Api.Infrastructure.Storage;

public static class AttachmentValidation
{
    public const long MaxSizeBytes = 10 * 1024 * 1024;

    private static readonly string[] AllowedExtensions =
    [
        ".png",
        ".jpg",
        ".jpeg",
        ".pdf",
        ".txt",
        ".docx",
        ".xlsx"
    ];

    public static Dictionary<string, string[]> Validate(IFormFile? file)
    {
        var errors = new Dictionary<string, string[]>();

        if (file is null)
        {
            errors["File"] = ["File is required."];
            return errors;
        }

        if (file.Length <= 0)
        {
            errors["File"] = ["File cannot be empty."];
        }

        if (file.Length > MaxSizeBytes)
        {
            errors["File"] = ["File cannot exceed 10 MB."];
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
        {
            errors["File"] = ["File type is not allowed."];
        }

        return errors;
    }
}
