using FluentAssertions;
using MS.Microservice.Domain.Aggregates.IdentityModel;
using Xunit;

namespace MS.Microservice.Core.Tests.Domain.IdentityModel;

public sealed class PasswordSaltHelperTests
{
    [Fact]
    public void Generate_ShouldReturnFourCharacterLowercaseAlphaNumericSalt()
    {
        string salt = PasswordSaltHelper.Generate();

        salt.Should().HaveLength(4);
        salt.Should().MatchRegex("^[0-9a-z]{4}$");
    }
}
