using Microsoft.AspNetCore.Mvc;
using MS.Microservice.AspNetCore.Encryption;
using MS.Microservice.Lab.Infrastructure.Labs;
using MS.Microservice.Lab.Infrastructure.Encryption;

namespace MS.Microservice.Lab.Controller;

[LabOnly]
[ApiController]
[Route("api/lab/encryption")]
public sealed class EncryptionController : ControllerBase
{
    [HttpPost("echo")]
    public IActionResult Echo([FromBody] EncryptedEchoRequest request) => Content(request.Message);

    [HttpPost("plain")]
    [NoEncrypt]
    public IActionResult Plain([FromBody] EncryptedEchoRequest request) => Content(request.Message);

    [HttpPost("ordinary")]
    public IActionResult Ordinary([FromBody] OrdinaryEchoRequest request) => Content(request.Message);
}

public sealed record OrdinaryEchoRequest(string Message);
