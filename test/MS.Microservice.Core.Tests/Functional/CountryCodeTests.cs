using FluentAssertions;
using MS.Microservice.Core.Functional;

namespace MS.Microservice.Core.Tests.Functional
{
    public class CountryCodeTests
    {
        [Fact]
        public void CountryCode_Constructor_StoresValue()
        {
            var cc = new CountryCode("cn");
            cc.ToString().Should().Be("cn");
        }

        [Fact]
        public void CountryCode_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new CountryCode(null!));
        }

        [Fact]
        public void CountryCode_ImplicitToString_ReturnsValue()
        {
            CountryCode cc = new("cn");
            string s = cc;
            s.Should().Be("cn");
        }

        [Fact]
        public void CountryCode_ImplicitFromString_CreatesCountryCode()
        {
            CountryCode cc = "cn";
            cc.ToString().Should().Be("cn");
        }

        [Fact]
        public void PhoneNumber_Create_RoundTrips()
        {
            var cc = new CountryCode("uk");
            var number = new PhoneNumber.Number();
            var pn = PhoneNumber.Create(PhoneNumber.NumberType.Mobile, cc, number);
            pn.Type.Should().Be(PhoneNumber.NumberType.Mobile);
            pn.CountryCode.ToString().Should().Be("uk");
            pn.ToString().Should().StartWith("Mobile: +uk ");
        }

        [Fact]
        public void ValidCountryCode_Uk_IsValid()
        {
            var cc = new CountryCode("uk");
            var result = PhoneNumber.ValidCountryCode(cc);
            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public void ValidCountryCode_Us_IsValid()
        {
            var cc = new CountryCode("us");
            var result = PhoneNumber.ValidCountryCode(cc);
            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public void ValidCountryCode_Unsupported_IsInvalid()
        {
            var cc = new CountryCode("cn");
            var result = PhoneNumber.ValidCountryCode(cc);
            result.IsInvalid.Should().BeTrue();
        }

        [Fact]
        public void ValidNumberType_Mobile_IsValid()
        {
            var result = PhoneNumber.ValidNumberType(PhoneNumber.NumberType.Mobile);
            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public void ValidNumberType_Home_IsValid()
        {
            var result = PhoneNumber.ValidNumberType(PhoneNumber.NumberType.Home);
            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public void ValidNumber_AnyValue_IsValid()
        {
            var result = PhoneNumber.ValidNumber(new PhoneNumber.Number());
            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public void CreatePhoneNumber_ValidInputs_ReturnsValid()
        {
            var result = PhoneNumber.CreatePhoneNumber(
                PhoneNumber.NumberType.Mobile,
                new CountryCode("uk"),
                new PhoneNumber.Number());

            result.IsValid.Should().BeTrue();
            result.Valid.Type.Should().Be(PhoneNumber.NumberType.Mobile);
        }
    }
}
