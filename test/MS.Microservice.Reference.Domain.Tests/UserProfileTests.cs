using MS.Microservice.Reference.Domain;
using Xunit;

namespace MS.Microservice.Reference.Domain.Tests;

public sealed class UserProfileTests
{
    [Fact]
    public void ExternalIdentityIsOpaqueAndSeparateFromLocalKey()
    {
        var profile = UserProfile.Create("https://issuer.example", "opaque/not-a-number", " 用户 ", ["EDITOR", "reader", "editor"], DateTimeOffset.UtcNow);
        Assert.NotEqual(Guid.Empty, profile.Id);
        Assert.Equal("opaque/not-a-number", profile.Subject);
        Assert.Equal("用户", profile.DisplayName);
        Assert.Equal(new[] { "editor", "reader" }, profile.Roles.Select(x => x.Name));
        Assert.Single(profile.DomainEvents);
    }

    [Theory]
    [InlineData("", "subject")]
    [InlineData("https://issuer.example", "")]
    [InlineData("not-an-issuer", "subject")]
    [InlineData("ftp://issuer.example", "subject")]
    public void RejectsInvalidExternalIdentity(string issuer, string subject)
        => Assert.Throws<ProfileValidationException>(() => UserProfile.Create(issuer, subject, "name", [], DateTimeOffset.UtcNow));

    [Fact]
    public void NoOpUpdateProducesNoNewEventOrVersion()
    {
        var profile = UserProfile.Create("https://issuer.example", "subject", "name", ["reader"], DateTimeOffset.UtcNow);
        profile.ClearDomainEvents();
        Assert.False(profile.Change(" name ", ["READER", "reader"], DateTimeOffset.UtcNow));
        Assert.Equal(1, profile.Version);
        Assert.Empty(profile.DomainEvents);
    }

    [Fact]
    public void InvalidUpdateLeavesAggregateUntouched()
    {
        var profile = UserProfile.Create("https://issuer.example", "subject", "name", ["reader"], DateTimeOffset.UtcNow);
        profile.ClearDomainEvents();
        Assert.Throws<ProfileValidationException>(() => profile.Change("changed", ["unknown"], DateTimeOffset.UtcNow));
        Assert.Equal("name", profile.DisplayName);
        Assert.Equal(1, profile.Version);
        Assert.Empty(profile.DomainEvents);
    }

    [Fact]
    public void RealUpdateKeepsRetainedRoleObjectsAndRaisesOneEvent()
    {
        var profile = UserProfile.Create("https://issuer.example", "subject", "name", ["reader"], DateTimeOffset.UtcNow);
        var retained = profile.Roles.Single();
        profile.ClearDomainEvents();
        Assert.True(profile.Change("changed", ["reader", "editor"], DateTimeOffset.UtcNow));
        Assert.Contains(retained, profile.Roles);
        Assert.Equal(2, profile.Version);
        Assert.Equal("profile.updated", Assert.Single(profile.DomainEvents).Action);
    }

    [Fact]
    public void DifferentIssuersMayHaveTheSameSubject()
    {
        var first = UserProfile.Create("https://first.example", "same", "name", [], DateTimeOffset.UtcNow);
        var second = UserProfile.Create("https://second.example", "same", "name", [], DateTimeOffset.UtcNow);
        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEqual(first.Issuer, second.Issuer);
    }
}
