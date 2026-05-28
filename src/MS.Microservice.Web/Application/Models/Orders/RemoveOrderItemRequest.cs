namespace MS.Microservice.Web.Application.Models.Orders
{
    public sealed record RemoveOrderItemRequest(string ProductId, int Quantity);
}
