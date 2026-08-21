using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MS.Microservice.Persistence.EFCore.Outbox;

namespace MS.Microservice.Web.Controller;

[ApiController]
[Authorize(Policy = "Manage")]
[Route("api/operations/outbox")]
public sealed class OutboxOperationsController(
    IOutboxStore outboxStore,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpPost("{messageId:guid}/replay")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReplayAsync(Guid messageId, CancellationToken cancellationToken)
    {
        var replayed = await outboxStore.ReplayDeadLetterAsync(
            messageId,
            timeProvider.GetUtcNow(),
            cancellationToken);
        if (replayed)
        {
            return NoContent();
        }

        return NotFound(new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "Dead-lettered outbox message not found.",
            Detail = "The message does not exist or is not currently dead-lettered."
        });
    }
}
