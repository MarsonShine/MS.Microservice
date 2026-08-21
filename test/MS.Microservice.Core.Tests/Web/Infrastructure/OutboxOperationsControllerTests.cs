using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MS.Microservice.Persistence.EFCore.Outbox;
using MS.Microservice.Web.Controller;
using NSubstitute;

namespace MS.Microservice.Core.Tests.Web.Infrastructure;

public sealed class OutboxOperationsControllerTests
{
    [Fact]
    public async Task ReplayAsync_DeadLetterExists_ReturnsNoContent()
    {
        var store = Substitute.For<IOutboxStore>();
        store.ReplayDeadLetterAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(true);
        var controller = new OutboxOperationsController(store, TimeProvider.System);

        var result = await controller.ReplayAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task ReplayAsync_MessageUnavailable_ReturnsSanitizedProblemDetails()
    {
        var controller = new OutboxOperationsController(
            Substitute.For<IOutboxStore>(),
            TimeProvider.System);

        var result = await controller.ReplayAsync(Guid.NewGuid(), CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(notFound.Value);
        Assert.Equal(404, problem.Status);
        Assert.DoesNotContain("exception", problem.Detail!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Controller_RequiresManageAuthorizationPolicy()
    {
        var attribute = Assert.Single(
            typeof(OutboxOperationsController).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
                .Cast<AuthorizeAttribute>());

        Assert.Equal("Manage", attribute.Policy);
    }
}
