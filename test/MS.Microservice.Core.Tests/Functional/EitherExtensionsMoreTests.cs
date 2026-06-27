using MS.Microservice.Core.Functional;
using Xunit;

namespace MS.Microservice.Core.Tests.Functional
{
    public class EitherExtensionsMoreTests
    {
        [Fact]
        public async Task LeftAsync_CreatesTaskWithLeft()
        {
            var task = EitherExtensions.LeftAsync<Error, int>(Error.Validation("bad"));
            var result = await task;
            Assert.True(result.IsLeft);
            Assert.Equal("validation", result.Left.Code);
        }

        [Fact]
        public async Task RightAsync_CreatesTaskWithRight()
        {
            var task = EitherExtensions.RightAsync<Error, int>(42);
            var result = await task;
            Assert.True(result.IsRight);
            Assert.Equal(42, result.Right);
        }

        [Fact]
        public async Task AsTask_CreatesTaskFromEither()
        {
            Either<Error, int> either = F.Right(42);
            var result = await either.AsTask();
            Assert.True(result.IsRight);
        }

        [Fact]
        public void Try_WhenSucceeds_ReturnsRight()
        {
            var result = EitherExtensions.Try(() => 42);
            Assert.True(result.IsRight);
            Assert.Equal(42, result.Right);
        }

        [Fact]
        public void Try_WhenThrows_ReturnsLeftWithDetails()
        {
            var result = EitherExtensions.Try<int>(() => throw new InvalidOperationException("boom"), code: "test");
            Assert.True(result.IsLeft);
            Assert.Equal("test", result.Left.Code);
            Assert.Contains("InvalidOperationException", result.Left.DetailsOrEmpty.Single());
        }

        [Fact]
        public void Try_Action_WhenSucceeds_ReturnsUnit()
        {
            var result = EitherExtensions.Try(() => { }, "action-test");
            Assert.True(result.IsRight);
            Assert.Equal(Unit.Default, result.Right);
        }

        [Fact]
        public void Try_Action_WhenThrows_ReturnsLeft()
        {
            var result = EitherExtensions.Try(() => throw new InvalidOperationException("action boom"), "action-test");
            Assert.True(result.IsLeft);
            Assert.Equal("action-test", result.Left.Code);
        }

        [Fact]
        public async Task TryAsync_Action_WhenSucceeds_ReturnsUnit()
        {
            var result = await EitherExtensions.TryAsync(() => Task.CompletedTask, "async-action");
            Assert.True(result.IsRight);
        }

        [Fact]
        public async Task TryAsync_Action_WhenThrows_ReturnsLeft()
        {
            var result = await EitherExtensions.TryAsync(
                () => throw new InvalidOperationException("async boom"), "async-action");
            Assert.True(result.IsLeft);
            Assert.Equal("async-action", result.Left.Code);
        }

        [Fact]
        public async Task Map_Task_TransformsRight()
        {
            Task<Either<Error, int>> task = Task.FromResult((Either<Error, int>)F.Right(21));
            var result = await task.Map(x => x * 2);
            Assert.True(result.IsRight);
            Assert.Equal(42, result.Right);
        }

        [Fact]
        public async Task Map_Task_WhenLeft_PreservesError()
        {
            Task<Either<Error, int>> task = Task.FromResult((Either<Error, int>)F.Left(Error.Validation("bad")));
            var result = await task.Map(x => x * 2);
            Assert.True(result.IsLeft);
            Assert.Equal("validation", result.Left.Code);
        }

        [Fact]
        public async Task MapLeft_Task_TransformsLeft()
        {
            Task<Either<Error, int>> task = Task.FromResult((Either<Error, int>)F.Left(Error.Validation("original")));
            var result = await task.MapLeft(e => Error.Conflict("mapped"));
            Assert.True(result.IsLeft);
            Assert.Equal("conflict", result.Left.Code);
        }

        [Fact]
        public async Task MapLeft_Task_WhenRight_LeavesRight()
        {
            Task<Either<Error, int>> task = Task.FromResult((Either<Error, int>)F.Right(42));
            var result = await task.MapLeft(e => Error.Conflict("ignored"));
            Assert.True(result.IsRight);
            Assert.Equal(42, result.Right);
        }

        [Fact]
        public async Task Bind_Task_BindsRight()
        {
            Task<Either<Error, int>> task = Task.FromResult((Either<Error, int>)F.Right(21));
            var result = await task.Bind(x => (Either<Error, int>)F.Right(x * 2));
            Assert.True(result.IsRight);
            Assert.Equal(42, result.Right);
        }

        [Fact]
        public async Task Bind_Task_WhenLeft_ShortCircuits()
        {
            Task<Either<Error, int>> task = Task.FromResult((Either<Error, int>)F.Left(Error.Validation("bad")));
            var result = await task.Bind(x => (Either<Error, int>)F.Right(x * 2));
            Assert.True(result.IsLeft);
            Assert.Equal("validation", result.Left.Code);
        }

        [Fact]
        public async Task BindAsync_Task_BindsRight()
        {
            Task<Either<Error, int>> task = Task.FromResult((Either<Error, int>)F.Right(21));
            var result = await task.BindAsync(x => Task.FromResult((Either<Error, int>)F.Right(x * 2)));
            Assert.True(result.IsRight);
            Assert.Equal(42, result.Right);
        }

        [Fact]
        public async Task BindAsync_Task_WhenLeft_ShortCircuits()
        {
            Task<Either<Error, int>> task = Task.FromResult((Either<Error, int>)F.Left(Error.Validation("bad")));
            var result = await task.BindAsync(x => Task.FromResult((Either<Error, int>)F.Right(x)));
            Assert.True(result.IsLeft);
        }

        [Fact]
        public async Task MapAsync_Task_TransformsRight()
        {
            Task<Either<Error, int>> task = Task.FromResult((Either<Error, int>)F.Right(21));
            var result = await task.MapAsync(x => Task.FromResult(x * 2));
            Assert.True(result.IsRight);
            Assert.Equal(42, result.Right);
        }

        [Fact]
        public async Task MapAsync_Task_WhenLeft_PreservesError()
        {
            Task<Either<Error, int>> task = Task.FromResult((Either<Error, int>)F.Left(Error.Validation("bad")));
            var result = await task.MapAsync(x => Task.FromResult(x * 2));
            Assert.True(result.IsLeft);
        }

        [Fact]
        public async Task Tap_Task_ExecutesEffect()
        {
            int captured = 0;
            Task<Either<Error, int>> task = Task.FromResult((Either<Error, int>)F.Right(42));
            var result = await task.Tap(x => captured = x);
            Assert.Equal(42, captured);
            Assert.True(result.IsRight);
        }

        [Fact]
        public async Task Tap_Task_WhenLeft_SkipsEffect()
        {
            int captured = 0;
            Task<Either<Error, int>> task = Task.FromResult((Either<Error, int>)F.Left(Error.Validation("bad")));
            var result = await task.Tap(x => captured = x);
            Assert.Equal(0, captured);
        }

        [Fact]
        public async Task TapAsync_Task_ExecutesEffect()
        {
            int captured = 0;
            Task<Either<Error, int>> task = Task.FromResult((Either<Error, int>)F.Right(42));
            var result = await task.TapAsync(x => { captured = x; return Task.CompletedTask; });
            Assert.Equal(42, captured);
            Assert.True(result.IsRight);
        }

        [Fact]
        public async Task TapAsync_Task_WhenLeft_SkipsEffect()
        {
            int captured = 0;
            Task<Either<Error, int>> task = Task.FromResult((Either<Error, int>)F.Left(Error.Validation("bad")));
            var result = await task.TapAsync(x => { captured = x; return Task.CompletedTask; });
            Assert.Equal(0, captured);
        }

        [Fact]
        public async Task Where_Task_WhenPredicateTrue_ReturnsRight()
        {
            Task<Either<Error, int>> task = Task.FromResult((Either<Error, int>)F.Right(42));
            var result = await task.Where(x => x > 0, x => Error.Validation("too small"));
            Assert.True(result.IsRight);
            Assert.Equal(42, result.Right);
        }

        [Fact]
        public async Task Where_Task_WhenPredicateFalse_ReturnsLeft()
        {
            Task<Either<Error, int>> task = Task.FromResult((Either<Error, int>)F.Right(-1));
            var result = await task.Where(x => x > 0, x => Error.Validation("negative"));
            Assert.True(result.IsLeft);
            Assert.Equal("validation", result.Left.Code);
        }

        [Fact]
        public async Task Where_Task_WhenLeft_PreservesError()
        {
            Task<Either<Error, int>> task = Task.FromResult((Either<Error, int>)F.Left(Error.Validation("bad")));
            var result = await task.Where(x => x > 0, x => Error.Validation("ignored"));
            Assert.True(result.IsLeft);
            Assert.Equal("validation", result.Left.Code);
        }

        [Fact]
        public async Task MatchAsync_Task_WithSyncCallbacks_Right()
        {
            Task<Either<Error, int>> task = Task.FromResult((Either<Error, int>)F.Right(42));
            var result = await task.MatchAsync(
                left: l => $"error: {l.Message}",
                right: r => $"ok: {r}");
            Assert.Equal("ok: 42", result);
        }

        [Fact]
        public async Task MatchAsync_Task_WithAsyncCallbacks_Right()
        {
            Task<Either<Error, int>> task = Task.FromResult((Either<Error, int>)F.Right(42));
            var result = await task.MatchAsync(
                left: l => Task.FromResult($"error: {l.Message}"),
                right: r => Task.FromResult($"ok: {r}"));
            Assert.Equal("ok: 42", result);
        }

        [Fact]
        public async Task MatchAsync_Task_WhenLeft_UsesLeftCallback()
        {
            Task<Either<Error, int>> task = Task.FromResult((Either<Error, int>)F.Left(Error.Validation("bad")));
            var result = await task.MatchAsync(
                left: l => $"error: {l.Message}",
                right: r => $"ok: {r}");
            Assert.Equal("error: bad", result);
        }

        [Fact]
        public async Task MatchAsync_Task_WhenLeft_AsyncCallback()
        {
            Task<Either<Error, int>> task = Task.FromResult((Either<Error, int>)F.Left(Error.Validation("bad")));
            var result = await task.MatchAsync(
                left: l => Task.FromResult($"error: {l.Message}"),
                right: r => Task.FromResult($"ok: {r}"));
            Assert.Equal("error: bad", result);
        }
    }
}
