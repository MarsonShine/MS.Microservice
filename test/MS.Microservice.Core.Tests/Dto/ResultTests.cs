using MS.Microservice.Core.Dto;
using MS.Microservice.Core.Functional;
using Xunit;

namespace MS.Microservice.Core.Tests.Dto
{
    public class ResultTests
    {
        [Fact]
        public void Success_HasValue()
        {
            var r = Result<int>.Success(42);
            Assert.True(r.IsSuccess);
            Assert.False(r.IsFailure);
            Assert.Equal(42, r.Value);
        }

        [Fact]
        public void Fail_HasError()
        {
            var err = new InvalidOperationException("boom");
            var r = Result<int>.Fail(err);
            Assert.True(r.IsFailure);
            Assert.False(r.IsSuccess);
            Assert.Equal("boom", r.Error.Message);
        }

        [Fact]
        public void ImplicitFromValue_CreatesSuccess()
        {
            Result<string> r = "hello";
            Assert.True(r.IsSuccess);
            Assert.Equal("hello", r.Value);
        }

        [Fact]
        public void Value_OnFailure_Throws()
        {
            var r = Result<int>.Fail(new Exception("err"));
            Assert.Throws<InvalidOperationException>(() => r.Value);
        }

        [Fact]
        public void Error_OnSuccess_Throws()
        {
            var r = Result<int>.Success(1);
            Assert.Throws<InvalidOperationException>(() => r.Error);
        }

        [Fact]
        public void Match_OnSuccess_CallsOnSuccess()
        {
            var r = Result<int>.Success(10);
            var result = r.Match(v => v * 2, e => 0);
            Assert.Equal(20, result);
        }

        [Fact]
        public void Match_OnFailure_CallsOnFailure()
        {
            var r = Result<int>.Fail(new InvalidOperationException("bad"));
            var result = r.Match(v => 0, e => -1);
            Assert.Equal(-1, result);
        }

        [Fact]
        public void Match_Action_OnSuccess()
        {
            var r = Result<int>.Success(5);
            int captured = 0;
            var unit = r.Match(v => { captured = v; }, e => { });
            Assert.Equal(5, captured);
            Assert.Equal(Unit.Default, unit);
        }

        [Fact]
        public void Match_Action_OnFailure()
        {
            var r = Result<int>.Fail(new InvalidOperationException("fail"));
            string captured = "";
            r.Match(v => { }, e => { captured = e.Message; });
            Assert.Equal("fail", captured);
        }

        [Fact]
        public void Map_OnSuccess_Transforms()
        {
            var r = Result<int>.Success(5);
            var mapped = r.Map(v => v * 2);
            Assert.True(mapped.IsSuccess);
            Assert.Equal(10, mapped.Value);
        }

        [Fact]
        public void Map_OnFailure_PreservesError()
        {
            var err = new InvalidOperationException("bad");
            var r = Result<int>.Fail(err);
            var mapped = r.Map(v => v * 2);
            Assert.True(mapped.IsFailure);
            Assert.Same(err, mapped.Error);
        }

        [Fact]
        public void Bind_OnSuccess_Chains()
        {
            var r = Result<int>.Success(5);
            var bound = r.Bind(v => Result<string>.Success($"val:{v}"));
            Assert.True(bound.IsSuccess);
            Assert.Equal("val:5", bound.Value);
        }

        [Fact]
        public void Bind_OnFailure_ShortCircuits()
        {
            var err = new InvalidOperationException("fail");
            var r = Result<int>.Fail(err);
            bool called = false;
            var bound = r.Bind(v => { called = true; return Result<string>.Success("x"); });
            Assert.True(bound.IsFailure);
            Assert.Same(err, bound.Error);
            Assert.False(called);
        }

        [Fact]
        public void Bind_CanFail()
        {
            var r = Result<int>.Success(5);
            var bound = r.Bind(v => Result<string>.Fail(new ArgumentException("invalid")));
            Assert.True(bound.IsFailure);
            Assert.Equal("invalid", bound.Error.Message);
        }
    }
}