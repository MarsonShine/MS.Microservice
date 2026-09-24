using MS.Microservice.Reference.Domain;
using Xunit;

namespace MS.Microservice.Reference.Domain.Tests;

public sealed class OrderTests
{
    [Fact]
    public void CreateNormalizesSkuAndPreservesQuantity()
    {
        var now = DateTimeOffset.UtcNow;
        var order = Order.Create("  " + new string('A', 64) + "  ", 3,
            "https://issuer.example", "owner", now);

        Assert.Equal(new string('A', 64), order.Sku);
        Assert.Equal(3, order.Quantity);
        Assert.Equal(now, order.CreatedAtUtc);
        Assert.Equal("https://issuer.example", order.OwnerIssuer);
        Assert.Equal("owner", order.OwnerSubject);
        Assert.NotEqual(Guid.Empty, order.Id);
    }

    [Theory]
    [InlineData("", 1)]
    [InlineData(" ", 1)]
    [InlineData("SKU", 0)]
    [InlineData("SKU", -1)]
    public void CreateRejectsInvalidSkuOrQuantity(string sku, int quantity)
        => Assert.Throws<OrderValidationException>(() => Order.Create(sku, quantity,
            "https://issuer.example", "owner", DateTimeOffset.UtcNow));

    [Fact]
    public void CreateRejectsOverlongSku()
        => Assert.Throws<OrderValidationException>(() => Order.Create(new string('A', 65), 1,
            "https://issuer.example", "owner", DateTimeOffset.UtcNow));

    [Theory]
    [InlineData("", "owner")]
    [InlineData("https://issuer.example", "")]
    [InlineData(" ", "owner")]
    [InlineData("https://issuer.example", " ")]
    public void CreateRejectsMissingOwner(string issuer, string subject)
        => Assert.Throws<OrderValidationException>(() => Order.Create("SKU", 1, issuer, subject, DateTimeOffset.UtcNow));

    [Fact]
    public void CreateRejectsOverlongOwnerFields()
    {
        Assert.Throws<OrderValidationException>(() => Order.Create("SKU", 1, new string('i', 513),
            "owner", DateTimeOffset.UtcNow));
        Assert.Throws<OrderValidationException>(() => Order.Create("SKU", 1, "https://issuer.example",
            new string('s', 256), DateTimeOffset.UtcNow));
    }
}
