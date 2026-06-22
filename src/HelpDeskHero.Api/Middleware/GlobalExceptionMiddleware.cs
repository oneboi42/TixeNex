using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskHero.Api.Middleware;

public sealed class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;

    public GlobalExceptionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task Invoke(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (DbUpdateConcurrencyException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            context.Response.ContentType = "application/json";

            var payload = new
            {
                code = "concurrency_conflict",
                message = "Dane zostały zmienione przez innego użytkownika. Odśwież widok i spróbuj ponownie."
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
        }
        catch (Exception)
        {
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            context.Response.ContentType = "application/json";

            var payload = new
            {
                code = "server_error",
                message = "Wystąpił nieoczekiwany błąd serwera."
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
        }
    }
}