using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MS.Microservice.AspNetCore;
using MS.Microservice.Idempotency.Mvc;
using MS.Microservice.Reference.Application;
using MS.Microservice.Reference.Web;

namespace MS.Microservice.Reference.Web.Tests;

[ApiController]
[Authorize(Policy = "Manage")]
[Route("test/mvc/profiles")]
public sealed class ProfileMvcTestController(ProfileService service, ExternalIdentityOptions identity) : ControllerBase
{
    [HttpPost]
    [RequireHttpIdempotency<ProfileCreateMvcIdempotencyFilter>]
    public Task<IActionResult> Create([FromBody] CreateProfile request) => CreateCoreAsync(request);

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
