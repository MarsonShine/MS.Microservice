using MS.Microservice.Core.Functional;
using Xunit;

namespace MS.Microservice.Core.Tests.Functional
{
    public class EitherTaskExtensionsTests
    {
        [Fact]
        public async Task BindAsync_WhenAllStepsSucceed_ComposesPipeline()
        {
            var result = await EitherExtensions.RightAsync<Error, int>(3)
                .Map(value => value + 1)
                .Bind(value =>
                {
                    Either<Error, int> next = F.Right(value * 2);
                    return next;
                })
                .BindAsync(value => Task.FromResult((Either<Error, string>)F.Right($"value:{value}")));

            Assert.True(result.IsRight);
            Assert.Equal("value:8", result.Right);
        }

        [Fact]
        public async Task BindAsync_WhenLeftValueExists_SkipsSubsequentStep()
        {
            var nextCalled = false;

            var result = await EitherExtensions.LeftAsync<Error, int>(Error.Validation("invalid"))
                .BindAsync(value =>
                {
                    nextCalled = true;
                    return Task.FromResult((Either<Error, string>)F.Right(value.ToString()));
                });

            Assert.True(result.IsLeft);
            Assert.False(nextCalled);
            Assert.Equal("validation", result.Left.Code);
        }
    }
}
