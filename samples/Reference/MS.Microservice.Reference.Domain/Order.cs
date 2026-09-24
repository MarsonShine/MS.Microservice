using MS.Microservice.Core.Domain;

namespace MS.Microservice.Reference.Domain;

public sealed class OrderValidationException(string message) : Exception(message);

public sealed class Order : IAggregateRoot
{
    public Guid Id { get; private set; }
    public string Sku { get; private set; } = "";
    public int Quantity { get; private set; }
    public string OwnerIssuer { get; private set; } = "";
    public string OwnerSubject { get; private set; } = "";
    public DateTimeOffset CreatedAtUtc { get; private set; }

    private Order() { }

    public static Order Create(string sku, int quantity, string ownerIssuer, string ownerSubject, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(sku) || sku.Trim().Length > 64)
            throw new OrderValidationException("SKU must contain 1..64 characters.");
        if (quantity < 1)
            throw new OrderValidationException("Quantity must be positive.");
        if (string.IsNullOrWhiteSpace(ownerIssuer) || ownerIssuer.Length > 512
            || string.IsNullOrWhiteSpace(ownerSubject) || ownerSubject.Length > 255)
            throw new OrderValidationException("A bounded owner issuer and subject are required.");
        if (now == default || now.Offset != TimeSpan.Zero)
            throw new OrderValidationException("A UTC creation time is required.");

        return new Order
        {
            Id = Guid.CreateVersion7(), Sku = sku.Trim(), Quantity = quantity,
            OwnerIssuer = ownerIssuer, OwnerSubject = ownerSubject, CreatedAtUtc = now
        };
    }
}
