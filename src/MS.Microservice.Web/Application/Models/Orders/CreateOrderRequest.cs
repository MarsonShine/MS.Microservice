namespace MS.Microservice.Web.Application.Models.Orders
{
    public sealed record CreateOrderRequest(string CustomerId, string Currency);
}
