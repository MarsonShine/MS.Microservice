namespace MS.Microservice.Lab.Application.Models.Orders
{
    public sealed record AddOrderItemRequest(string ProductId, decimal UnitPrice, int Quantity);
}
