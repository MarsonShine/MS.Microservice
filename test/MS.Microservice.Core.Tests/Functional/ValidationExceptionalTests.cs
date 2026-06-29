using MS.Microservice.Core.Functional;
using Xunit;

namespace MS.Microservice.Core.Tests.Functional
{
    public class ValidationExceptionalTests
    {
        // === Validation tests ===
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

        // === Exceptional<T> from value/implicit ===
        [Fact]
        public void Exceptional_FromSuccess_IsSuccess()
        {
            Exceptional<int> ex = F.Success(42);
            Assert.True(ex.IsSuccess);
            Assert.False(ex.IsException);
            Assert.Equal(42, ex.Success);
        }

        [Fact]
        public void Exceptional_FromExceptionThrown_IsException()
        {
            var err = new InvalidOperationException("boom");
            Exceptional<int> ex = F.ExceptionThrown(err);
            Assert.True(ex.IsException);
            Assert.False(ex.IsSuccess);
            Assert.Equal("boom", ex.Exception.Message);
        }

        [Fact]
        public void Exceptional_ImplicitFromValue_CreatesSuccess()
        {
            Exceptional<string> ex = "hello";
            Assert.True(ex.IsSuccess);
            Assert.Equal("hello", ex.Success);
        }

        [Fact]
        public void Exceptional_ImplicitFromException_CreatesException()
        {
            Exceptional<string> ex = new ArgumentException("bad");
            Assert.True(ex.IsException);
            Assert.Equal("bad", ex.Exception.Message);
        }

        [Fact]
        public void Exceptional_SuccessProperty_WhenException_Throws()
        {
            Exceptional<int> ex = new InvalidOperationException("err");
            Assert.Throws<InvalidOperationException>(() => ex.Success);
        }

        [Fact]
        public void Exceptional_ExceptionProperty_WhenSuccess_Throws()
        {
            Exceptional<int> ex = F.Success(1);
            Assert.Throws<InvalidOperationException>(() => ex.Exception);
        }

        // === Try / TryAsync ===
        [Fact]
        public void Exceptional_Try_Success()
        {
            var result = ExceptionalExtensions.Try(() => 42);
            Assert.True(result.IsSuccess);
            Assert.Equal(42, result.Success);
        }

        [Fact]
        public void Exceptional_Try_Throws_ReturnsExceptionState()
        {
            var result = ExceptionalExtensions.Try<int>(() => throw new InvalidOperationException("boom"));
            Assert.True(result.IsException);
            Assert.Equal("boom", result.Exception.Message);
        }

        [Fact]
        public void Exceptional_Try_Action_Success()
        {
            int side = 0;
            var result = ExceptionalExtensions.Try(() => { side = 42; });
            Assert.True(result.IsSuccess);
            Assert.Equal(42, side);
        }

        [Fact]
        public void Exceptional_Try_Action_Throws()
        {
            var result = ExceptionalExtensions.Try(() => throw new ArgumentException("fail"));
            Assert.True(result.IsException);
            Assert.Equal("fail", result.Exception.Message);
        }

        [Fact]
        public async Task Exceptional_TryAsync_Success()
        {
            var result = await ExceptionalExtensions.TryAsync(() => Task.FromResult(99));
            Assert.True(result.IsSuccess);
            Assert.Equal(99, result.Success);
        }

        [Fact]
        public async Task Exceptional_TryAsync_Throws()
        {
            var result = await ExceptionalExtensions.TryAsync<int>(() => throw new InvalidOperationException("async fail"));
            Assert.True(result.IsException);
            Assert.Equal("async fail", result.Exception.Message);
        }

        [Fact]
        public async Task Exceptional_TryAsync_Action_Success()
        {
            var result = await ExceptionalExtensions.TryAsync(() => Task.CompletedTask);
            Assert.True(result.IsSuccess);
        }

        [Fact]
        public async Task Exceptional_TryAsync_Action_Throws()
        {
            var result = await ExceptionalExtensions.TryAsync(() => Task.FromException(new ArgumentException("task fail")));
            Assert.True(result.IsException);
            Assert.Equal("task fail", result.Exception.Message);
        }

        // === Match ===
        [Fact]
        public void Exceptional_Match_Success()
        {
            Exceptional<int> ex = F.Success(10);
            var result = ex.Match(e => $"err:{e.Message}", v => $"val:{v}");
            Assert.Equal("val:10", result);
        }

        [Fact]
        public void Exceptional_Match_Exception()
        {
            Exceptional<int> ex = new InvalidOperationException("bad");
            var result = ex.Match(e => $"err:{e.Message}", v => $"val:{v}");
            Assert.Equal("err:bad", result);
        }

        // === Map ===
        [Fact]
        public void Exceptional_Map_Success()
        {
            Exceptional<int> ex = F.Success(5);
            var result = ex.Map(v => v * 2);
            Assert.True(result.IsSuccess);
            Assert.Equal(10, result.Success);
        }

        [Fact]
        public void Exceptional_Map_Exception_PreservesError()
        {
            Exceptional<int> ex = new InvalidOperationException("err");
            var result = ex.Map(v => v * 2);
            Assert.True(result.IsException);
            Assert.Equal("err", result.Exception.Message);
        }

        // === Bind ===
        [Fact]
        public void Exceptional_Bind_Success()
        {
            Exceptional<int> ex = F.Success(41);
            var result = ex.Bind(v => (Exceptional<int>)F.Success(v + 1));
            Assert.True(result.IsSuccess);
            Assert.Equal(42, result.Success);
        }

        [Fact]
        public void Exceptional_Bind_Exception_PreservesError()
        {
            Exceptional<int> ex = new InvalidOperationException("fail");
            var result = ex.Bind(v => (Exceptional<int>)F.Success(v + 1));
            Assert.True(result.IsException);
            Assert.Equal("fail", result.Exception.Message);
        }

        // === Equals & operators ===
        [Fact]
        public void Exceptional_Equals_SameSuccess_True()
        {
            Exceptional<int> a = F.Success(1);
            Exceptional<int> b = F.Success(1);
            Assert.True(a.Equals(b));
            Assert.True(a == b);
            Assert.False(a != b);
        }

        [Fact]
        public void Exceptional_Equals_DifferentSuccess_False()
        {
            Exceptional<int> a = F.Success(1);
            Exceptional<int> b = F.Success(2);
            Assert.False(a.Equals(b));
            Assert.False(a == b);
            Assert.True(a != b);
        }

        [Fact]
        public void Exceptional_Equals_SuccessVsException_False()
        {
            Exceptional<int> a = F.Success(1);
            Exceptional<int> b = new InvalidOperationException("e");
            Assert.False(a.Equals(b));
        }

        [Fact]
        public void Exceptional_Equals_Boxed_Works()
        {
            object a = F.Success(5);
            object b = F.Success(5);
            Assert.True(a.Equals(b));
        }

        [Fact]
        public void Exceptional_Equals_DifferentType_False()
        {
            Exceptional<int> a = F.Success(5);
            Assert.False(a.Equals("not_an_exceptional"));
        }

        [Fact]
        public void Exceptional_GetHashCode_SameSuccess_Same()
        {
            Exceptional<int> a = F.Success(42);
            Exceptional<int> b = F.Success(42);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        // === ToString ===
        [Fact]
        public void Exceptional_ToString_Success()
        {
            Exceptional<int> ex = F.Success(42);
            Assert.Equal("Success(42)", ex.ToString());
        }

        [Fact]
        public void Exceptional_ToString_Exception()
        {
            Exceptional<int> ex = new InvalidOperationException("bad thing");
            Assert.Equal("ExceptionThrown(bad thing)", ex.ToString());
        }

        // === Implicit to/from Either ===
        [Fact]
        public void Exceptional_Either_Roundtrip()
        {
            Exceptional<int> ex = F.Success(10);
            Either<Exception, int> either = ex;
            Assert.True(either.IsRight);
            Assert.True(either.IsRight);
        }

        [Fact]
        public void Exceptional_FromEither_Success()
        {
            Either<Exception, int> either = F.Right(20);
            Exceptional<int> ex = either;
            Assert.True(ex.IsSuccess);
            Assert.Equal(20, ex.Success);
        }

        // === ExceptionThrown / Success struct tests ===
        [Fact]
        public void ExceptionThrown_NullCtor_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new ExceptionThrown(null!));
        }

        [Fact]
        public void SuccessStruct_NullCtor_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new Success<string>(null!));
        }

        [Fact]
        public void ExceptionThrown_ToString_Works()
        {
            var et = new ExceptionThrown(new InvalidOperationException("err"));
            Assert.Equal("ExceptionThrown(err)", et.ToString());
        }

        [Fact]
        public void SuccessStruct_ToString_Works()
        {
            var s = new Success<int>(42);
            Assert.Equal("Success(42)", s.ToString());
        }
    }
}