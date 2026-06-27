using System;
using System.Threading.Tasks;
using MS.Microservice.Core.Dto;
using MS.Microservice.Core.Functional;
using static MS.Microservice.Core.Extension.ResultExtensions;
using Xunit;

namespace MS.Microservice.Core.Tests.Coverage
{
    public class ResultExtensionsAsyncTests
    {
        [Fact] public void Try_Success() { var r = Try(() => 42); Assert.True(r.IsSuccess); Assert.Equal(42, r.Value); }
        [Fact] public void Try_Failure() { var r = Try<int>(() => throw new InvalidOperationException("fail")); Assert.True(r.IsFailure); }
        [Fact] public void Try_Action_Success() { var r = Try(() => { _ = 1 + 1; }); Assert.True(r.IsSuccess); }
        [Fact] public void Try_Action_Failure() { var r = Try(() => throw new InvalidOperationException("fail")); Assert.True(r.IsFailure); }

        [Fact] public async Task TryAsync_Success() { var r = await TryAsync(() => Task.FromResult(42)); Assert.True(r.IsSuccess); Assert.Equal(42, r.Value); }
        [Fact] public async Task TryAsync_Failure() { var r = await TryAsync<int>(() => Task.FromException<int>(new InvalidOperationException("fail"))); Assert.True(r.IsFailure); }
        [Fact] public async Task TryAsync_Action_Success() { var r = await TryAsync(() => Task.CompletedTask); Assert.True(r.IsSuccess); }
        [Fact] public async Task TryAsync_Action_Failure() { var r = await TryAsync(() => Task.FromException(new InvalidOperationException("fail"))); Assert.True(r.IsFailure); }

        // TapAsync: taps on success, passes through on failure
        [Fact] public async Task TapAsync_Success_ExecutesEffect()
        {
            var r = Result<int>.Success(42);
            int sideEffect = 0;
            var result = await r.TapAsync(x => { sideEffect = x; return Task.CompletedTask; });
            Assert.True(result.IsSuccess);
            Assert.Equal(42, result.Value);
            Assert.Equal(42, sideEffect);
        }

        [Fact] public async Task TapAsync_Failure_SkipsEffect()
        {
            var r = Result<int>.Fail(new InvalidOperationException("err"));
            int sideEffect = 0;
            var result = await r.TapAsync(x => { sideEffect = x; return Task.CompletedTask; });
            Assert.True(result.IsFailure);
            Assert.Equal(0, sideEffect);
        }

        [Fact] public async Task TapAsync_EffectThrows_ReturnsFailure()
        {
            var r = Result<string>.Success("ok");
            var result = await r.TapAsync(_ => throw new InvalidOperationException("effect fail"));
            Assert.True(result.IsFailure);
            Assert.Contains("effect fail", result.Error.Message);
        }

        // MatchAsync
        [Fact] public async Task MatchAsync_Success_ReturnsMappedValue()
        {
            var r = Result<int>.Success(42);
            var result = await r.MatchAsync(
                onSuccess: x => Task.FromResult($"ok:{x}"),
                onFailure: e => Task.FromResult($"err:{e.Message}"));
            Assert.Equal("ok:42", result);
        }

        [Fact] public async Task MatchAsync_Failure_ReturnsMappedError()
        {
            var r = Result<int>.Fail(new InvalidOperationException("boom"));
            var result = await r.MatchAsync(
                onSuccess: x => Task.FromResult($"ok:{x}"),
                onFailure: e => Task.FromResult($"err:{e.Message}"));
            Assert.Equal("err:boom", result);
        }
    }
}
