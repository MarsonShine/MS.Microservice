using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using MS.Microservice.AspNetCore;
using MS.Microservice.Idempotency.Mvc;
using MS.Microservice.Reference.Application;
using MS.Microservice.Reference.Web.HttpIdempotency;

namespace MS.Microservice.Reference.Web.Tests;

[ApiController]
[Authorize(Policy = "Manage")]
[Route("test/mvc/profiles")]
public sealed class ProfileMvcTestController(ProfileService service, ExternalIdentityOptions identity) : ControllerBase
{
    [HttpPost]
    [RequireHttpIdempotency<ReferenceHttpIdempotencyResourceFilter>("profiles.create")]
    public Task<IActionResult> Create([FromBody] CreateProfile request) => CreateCoreAsync(request);

    [HttpPost("alternate")]
    [RequireHttpIdempotency<ReferenceHttpIdempotencyResourceFilter>("profiles.alternate")]
    public Task<IActionResult> Alternate([FromBody] CreateProfile request) => CreateCoreAsync(request);

    [HttpPost("echo")]
    [RequireHttpIdempotency<ReferenceHttpIdempotencyResourceFilter>("profiles.echo")]
    public IActionResult Echo([FromBody] CreateProfile request) => Ok(request.DisplayName);

    [HttpPost("json")]
    [RequireHttpIdempotency<ReferenceHttpIdempotencyResourceFilter>("profiles.json")]
    public IActionResult Json([FromBody] JsonElement request) => Ok(request);

    [HttpPost("empty")]
    [RequireHttpIdempotency<ReferenceHttpIdempotencyResourceFilter>("profiles.empty")]
    public IActionResult ReturnNoContent([FromBody] CreateProfile request) => NoContent();

    [HttpPost("plain")]
    public Task<IActionResult> Plain([FromBody] CreateProfile request) => CreateCoreAsync(request);

    private async Task<IActionResult> CreateCoreAsync(CreateProfile request)
    {
        var actor = new AuditActor(User.FindFirst("iss")?.Value ?? "",
            User.FindFirst(identity.SubjectClaimType)?.Value ?? "");
        var result = await service.CreateAsync(request, actor, HttpContext.RequestAborted);
        return result.Match<IActionResult>(
            error => new HttpResultActionResult(ApplicationErrorResults.ToProblem(
                error.Code, error.Message, error.Details)),
            profile => Created($"/api/v1/profiles/{profile.Id}", profile));
    }
}
