using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MS.Microservice.Idempotency.Mvc;

public sealed class HttpResultActionResult(IResult result) : IActionResult
{
    public Task ExecuteResultAsync(ActionContext context) => result.ExecuteAsync(context.HttpContext);
}
