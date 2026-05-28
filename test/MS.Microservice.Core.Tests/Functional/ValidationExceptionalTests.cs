using MS.Microservice.Core.Functional;
using Xunit;

namespace MS.Microservice.Core.Tests.Functional
{
    public class ValidationExceptionalTests
    {
        [Fact]
        public void Validation_WhenCreatedFromValid_IsValid()
        {
            Validation<int> validation = F.Valid(42);

            Assert.True(validation.IsValid);
            Assert.Equal(42, validation.Valid);
        }

        [Fact]
        public void Validation_Map_WhenInvalid_PreservesError()
        {
            Validation<int> validation = F.Invalid(Error.Validation("bad input"));

            var result = validation.Map(value => value + 1);

            Assert.True(result.IsInvalid);
            Assert.Equal("validation", result.Invalid.Code);
        }

        [Fact]
        public void Exceptional_Try_WhenThrows_ReturnsExceptionState()
        {
            var result = ExceptionalExtensions.Try<int>(() => throw new InvalidOperationException("boom"));

            Assert.True(result.IsException);
            Assert.Equal("boom", result.Exception.Message);
        }

        [Fact]
        public void Exceptional_Bind_WhenSuccess_Composes()
        {
            Exceptional<int> exceptional = F.Success(41);

            var result = exceptional.Bind(value => (Exceptional<int>)F.Success(value + 1));

            Assert.True(result.IsSuccess);
            Assert.Equal(42, result.Success);
        }
    }
}
