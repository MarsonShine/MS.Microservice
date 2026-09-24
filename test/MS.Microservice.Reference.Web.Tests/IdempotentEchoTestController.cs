using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MS.Microservice.Idempotency.Mvc;
using MS.Microservice.Reference.Web;

namespace MS.Microservice.Reference.Web.Tests;

[ApiController]
[Authorize]
[Route("test/mvc/echo")]
public sealed class IdempotentEchoTestController : ControllerBase
{
    [HttpPost]
    [RequireHttpIdempotency<ReferenceHttpIdempotencyResourceFilter>("echo.create")]
    public IActionResult Post([FromBody] JsonElement request) => Ok(request);
}
