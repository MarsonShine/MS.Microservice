using Microsoft.AspNetCore.Mvc;
using MS.Microservice.Core.Functional;
using MS.Microservice.Domain.Exception;
using System.Diagnostics;

namespace MS.Microservice.Web.Infrastructure.Http;

public static class ApiProblemDetails
{
    public const string ContentType = "application/problem+json";
    public const string UnexpectedDetail = "服务器处理请求时发生意外错误。";

    public static ObjectResult ToProblem(this ControllerBase controller, Error error)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(error);

        var problem = FromError(controller.HttpContext, error);
        var result = new ObjectResult(problem)
        {
            DeclaredType = typeof(ProblemDetails),
            StatusCode = problem.Status
        };
        result.ContentTypes.Add(ContentType);
        return result;
    }

    public static ProblemDetails FromError(HttpContext context, Error error)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(error);

        var status = error.Code switch
        {
            "validation" => StatusCodes.Status400BadRequest,
            "unauthorized" => StatusCodes.Status401Unauthorized,
            "not_found" => StatusCodes.Status404NotFound,
            "conflict" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };
        var detail = status >= StatusCodes.Status500InternalServerError
            ? UnexpectedDetail
            : error.Message;
        var problem = Create(context, status, GetTitle(status), detail, error.Code);
        if (status < StatusCodes.Status500InternalServerError && error.DetailsOrEmpty.Count > 0)
        {
            problem.Extensions["errors"] = error.DetailsOrEmpty;
        }

        return problem;
    }

    public static ProblemDetails FromException(HttpContext context, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is DomainException domainException)
        {
            var status = domainException.Code is >= 400 and <= 499
                ? domainException.Code
                : StatusCodes.Status400BadRequest;
            return Create(context, status, GetTitle(status), domainException.Message, "domain_error");
        }

        return Create(
            context,
            StatusCodes.Status500InternalServerError,
            GetTitle(StatusCodes.Status500InternalServerError),
            UnexpectedDetail,
            "unexpected");
    }

    private static ProblemDetails Create(
        HttpContext context,
        int status,
        string title,
        string detail,
        string code)
    {
        var problem = new ProblemDetails
        {
            Type = "about:blank",
            Title = title,
            Status = status,
            Detail = detail,
            Instance = context.Request.Path
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;
        return problem;
    }

    private static string GetTitle(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "Bad Request",
        StatusCodes.Status401Unauthorized => "Unauthorized",
        StatusCodes.Status404NotFound => "Not Found",
        StatusCodes.Status409Conflict => "Conflict",
        _ => "Internal Server Error"
    };
}
