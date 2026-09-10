namespace MS.Microservice.Lab.Application.Models.Orders
{
    public sealed record RemoveOrderItemRequest(string ProductId, int Quantity);
}
