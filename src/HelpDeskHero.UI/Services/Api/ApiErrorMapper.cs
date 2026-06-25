using System.Net;
using System.Text.Json;

namespace HelpDeskHero.UI.Services.Api;

public static class ApiErrorMapper
{
    public static async Task<string> ToMessageAsync(
        HttpResponseMessage response,
        string fallbackMessage = "Wystąpił błąd podczas komunikacji z API.")
    {
        var apiMessage = await TryReadProblemDetailsAsync(response);

        if (!string.IsNullOrWhiteSpace(apiMessage))
            return apiMessage;

        return response.StatusCode switch
        {
            HttpStatusCode.BadRequest => "Nieprawidłowe dane. Sprawdź formularz i spróbuj ponownie.",
            HttpStatusCode.Unauthorized => "Sesja wygasła. Zaloguj się ponownie.",
            HttpStatusCode.Forbidden => "Brak uprawnień do wykonania tej operacji.",
            HttpStatusCode.NotFound => "Nie znaleziono wymaganego zasobu.",
            HttpStatusCode.Conflict => "Dane zostały zmienione przez innego użytkownika. Odśwież widok i spróbuj ponownie.",
            HttpStatusCode.InternalServerError => "Wystąpił błąd serwera. Spróbuj ponownie później.",
            _ => fallbackMessage
        };
    }

    private static async Task<string?> TryReadProblemDetailsAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();

        if (string.IsNullOrWhiteSpace(content))
            return null;

        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;

            var validationMessages = ReadValidationMessages(root);
            if (validationMessages.Count > 0)
                return string.Join('\n', validationMessages);

            if (TryGetString(root, "detail", out var detail))
                return detail;

            if (TryGetString(root, "message", out var message))
                return message;

            if (TryGetString(root, "title", out var title))
                return title;
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static List<string> ReadValidationMessages(JsonElement root)
    {
        var messages = new List<string>();

        if (!root.TryGetProperty("errors", out var errorsElement) ||
            errorsElement.ValueKind != JsonValueKind.Object)
        {
            return messages;
        }

        foreach (var property in errorsElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var item in property.Value.EnumerateArray())
            {
                var text = item.GetString();

                if (!string.IsNullOrWhiteSpace(text))
                    messages.Add($"{property.Name}: {text}");
            }
        }

        return messages;
    }

    private static bool TryGetString(JsonElement root, string propertyName, out string value)
    {
        value = string.Empty;

        if (!root.TryGetProperty(propertyName, out var element))
            return false;

        if (element.ValueKind != JsonValueKind.String)
            return false;

        value = element.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }
}
