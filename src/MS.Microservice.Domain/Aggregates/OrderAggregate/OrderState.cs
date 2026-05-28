using System.Collections.Immutable;

namespace MS.Microservice.Domain.Aggregates.OrderAggregate
{
    public sealed record OrderLineState(string ProductId, decimal UnitPrice, int Quantity)
    {
        public decimal Amount => UnitPrice * Quantity;
    }

    public sealed record OrderState(
        bool Exists,
        bool IsConfirmed,
        bool IsCancelled,
        string? CustomerId,
        string? Currency,
        ImmutableDictionary<string, OrderLineState> Lines,
        decimal TotalAmount,
        int Version)
    {
        public static OrderState Initial => new(
            false,
            false,
            false,
            null,
            null,
            ImmutableDictionary<string, OrderLineState>.Empty,
            0m,
            0);
    }
}
