namespace MS.Microservice.Web.Application.Models.Orders
{
    public sealed record AddOrderItemRequest(string ProductId, decimal UnitPrice, int Quantity);
}
