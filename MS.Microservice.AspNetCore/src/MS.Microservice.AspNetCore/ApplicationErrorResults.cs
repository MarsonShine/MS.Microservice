using Microsoft.AspNetCore.Http;

namespace MS.Microservice.AspNetCore;

/// <summary>Maps stable application error codes to public HTTP responses.</summary>
public static class ApplicationErrorResults
{
    public static IResult ToProblem(string code, string message, IReadOnlyList<string>? details = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(message);

        var status = code switch
        {
            "validation" => StatusCodes.Status400BadRequest,
            "unauthorized" => StatusCodes.Status401Unauthorized,
            "not_found" => StatusCodes.Status404NotFound,
            "conflict" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };
        var publicError = status < 500;
        var extensions = new Dictionary<string, object?>
        {
            ["code"] = publicError ? code : "unexpected"
        };
        if (publicError && details is { Count: > 0 })
            extensions["details"] = details.ToArray();

        return Results.Problem(
            statusCode: status,
            title: publicError ? message : "An unexpected error occurred.",
            extensions: extensions);
    }
}
