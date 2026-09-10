namespace MS.Microservice.Lab.Application.Models.Orders
{
    public sealed record CreateOrderRequest(string CustomerId, string Currency);
}
