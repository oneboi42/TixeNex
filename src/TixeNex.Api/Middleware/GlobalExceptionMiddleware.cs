using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace TixeNex.Api.Middleware;

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
            await WriteProblemAsync(
                context,
                HttpStatusCode.Conflict,
                "Konflikt danych",
                "The data was changed by another user. Refresh the page and try again.",
                "concurrency_conflict");
        }
        catch (Exception)
        {
            await WriteProblemAsync(
                context,
                HttpStatusCode.InternalServerError,
                "Server error",
                "An unexpected server error occurred.",
                "server_error");
        }
    }

    private static async Task WriteProblemAsync(
        HttpContext context,
        HttpStatusCode statusCode,
        string title,
        string detail,
        string code)
    {
        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/problem+json";

        var problem = new ProblemDetails
        {
            Status = (int)statusCode,
            Title = title,
            Detail = detail,
            Type = $"https://httpstatuses.com/{(int)statusCode}",
            Instance = context.Request.Path
        };

        problem.Extensions["code"] = code;

        var json = JsonSerializer.Serialize(problem, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);
    }
}
