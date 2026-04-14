using MS.Microservice.Core.Functional;
using System.Linq;
using Xunit;

namespace MS.Microservice.Core.Tests.Functional
{
    public class EitherTests
    {
        [Fact]
        public void Map_WhenRight_TransformsRightValue()
        {
            Either<Error, int> either = F.Right(41);

            var result = either.Map(value => value + 1);

            Assert.True(result.IsRight);
            Assert.Equal(42, result.Right);
        }

        [Fact]
        public void Bind_WhenLeft_ShortCircuits()
        {
            Either<Error, int> either = F.Left(Error.Validation("bad input"));

            var result = either.Bind(value => (Either<Error, int>)F.Right(value + 1));

            Assert.True(result.IsLeft);
            Assert.Equal("validation", result.Left.Code);
        }

        [Fact]
        public async Task TryAsync_WhenOperationThrows_ReturnsLeftWithDetails()
        {
            var result = await EitherExtensions.TryAsync<int>(() => throw new InvalidOperationException("boom"), code: "demo");

            Assert.True(result.IsLeft);
            Assert.Equal("demo", result.Left.Code);
            Assert.Contains("InvalidOperationException", result.Left.DetailsOrEmpty.Single());
        }
    }
}
