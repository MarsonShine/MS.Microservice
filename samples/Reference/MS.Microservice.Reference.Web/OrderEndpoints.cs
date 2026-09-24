using Microsoft.AspNetCore.Routing;
using MS.Microservice.AspNetCore;
using MS.Microservice.Reference.Application;
using MS.Microservice.Reference.Web.HttpIdempotency;

namespace MS.Microservice.Reference.Web;

internal static class OrderEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/api/v1/orders").RequireAuthorization();
        orders.MapPost("", async (CreateOrder request, OrderService service, HttpContext http,
            ExternalIdentityOptions identity, CancellationToken token) =>
        {
            var result = await service.CreateAsync(request, ReferenceActor.From(http.User, identity), token);
            return result.Match<IResult>(
                error => ApplicationErrorResults.ToProblem(error.Code, error.Message, error.Details),
                order => Results.Created($"/api/v1/orders/{order.Id}", order));
        }).RequireHttpIdempotency("orders.create");
        orders.MapGet("/{id:guid}", async (Guid id, IOrderRepository repository, HttpContext http,
            ExternalIdentityOptions identity, CancellationToken token) =>
            await repository.GetAsync(id, ReferenceActor.From(http.User, identity), token) is { } order
                ? Results.Ok(OrderView.From(order)) : Results.NotFound());
    }
}
