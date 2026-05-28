namespace MS.Microservice.Web.Application.Models.Orders
{
    public sealed record OrderEventResponse(string EventType, int Version, long GlobalPosition, DateTimeOffset CreatedAt);

    public sealed record OrderLineResponse(string ProductId, decimal UnitPrice, int Quantity, decimal Amount);

    public sealed record OrderDetailsResponse(
        Guid OrderId,
        string StreamId,
        int Version,
        string? CustomerId,
        string? Currency,
        string Status,
        decimal TotalAmount,
        IReadOnlyList<OrderLineResponse> Lines,
        IReadOnlyList<OrderEventResponse> Events,
        DateTimeOffset? ReadModelUpdatedAt);

    public sealed record OrderCommandResult(
        Guid OrderId,
        string StreamId,
        int ExpectedVersion,
        int CurrentVersion,
        IReadOnlyList<string> EventTypes,
        decimal TotalAmount,
        string Status);
}
