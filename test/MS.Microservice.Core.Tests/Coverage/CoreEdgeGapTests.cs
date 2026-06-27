using MS.Microservice.Core;
using MS.Microservice.Core.Functional;
using Xunit;

namespace MS.Microservice.Core.Tests.Coverage
{
    public class CoreEdgeGapTests
    {
        // ===== Error factories - Unauthorized, Unexpected, FromException =====
        [Fact] public void Error_Unauthorized() { var e = Error.Unauthorized("no access"); Assert.Equal("unauthorized", e.Code); Assert.Equal("no access", e.Message); }
        [Fact] public void Error_Unauthorized_WithDetails() { var e = Error.Unauthorized("no access", ["missing role", "expired token"]); Assert.Equal(2, e.DetailsOrEmpty.Count); }
        [Fact] public void Error_Unexpected() { var e = Error.Unexpected("oops"); Assert.Equal("unexpected", e.Code); }
        [Fact] public void Error_Unexpected_WithDetails() { var e = Error.Unexpected("oops", ["stack trace"]); Assert.Single(e.DetailsOrEmpty); }
        [Fact] public void Error_FromException() { var ex = new System.InvalidOperationException("bad state"); var e = Error.FromException(ex); Assert.Equal("unexpected", e.Code); Assert.Contains("InvalidOperationException", e.DetailsOrEmpty[0]); }
        [Fact] public void Error_FromException_CustomCode() { var e = Error.FromException(new System.ArgumentException("invalid"), "arg_error"); Assert.Equal("arg_error", e.Code); }
        [Fact] public void Error_ToDisplayMessage_WithDetails() { var e = Error.Validation("bad", ["field1", "field2"]); var msg = e.ToDisplayMessage(); Assert.Contains("field1", msg); Assert.Contains("field2", msg); Assert.Contains("bad", msg); }
        [Fact] public void Error_ToDisplayMessage_NoDetails() { var e = Error.Validation("simple"); Assert.Equal("simple", e.ToDisplayMessage()); }

        // ===== CorePlatformException =====
        [Fact] public void CorePlatformException_Default() { var ex = new CorePlatformException(); Assert.Equal(0, ex.Code); }
        [Fact] public void CorePlatformException_WithCode() { var ex = new CorePlatformException(404, "not found"); Assert.Equal(404, ex.Code); Assert.Equal("not found", ex.Message); }
        [Fact] public void CorePlatformException_WithInner() { var inner = new System.Exception(); var ex = new CorePlatformException("err", inner); Assert.Equal(inner, ex.InnerException); }
        [Fact] public void CorePlatformException_CodeProperty() { var ex = new CorePlatformException(500, "server error") { Code = 503 }; Assert.Equal(503, ex.Code); }

        // ===== F - Remainder =====
        [Fact] public void F_Remainder_Positive() { Assert.Equal(3, F.Remainder(13, 5)); }
        [Fact] public void F_Remainder_Negative() { Assert.Equal(2, F.Remainder(-13, 5)); }
        [Fact] public void F_Remainder_Zero() { Assert.Equal(0, F.Remainder(10, 5)); }

        // ===== F - ApplyR =====
        [Fact] public void F_ApplyR_3Args() { System.Func<int, int, int> mul = (a, b) => a * b; var f = F.ApplyR<int, int, int>(mul, 10); Assert.Equal(20, f(2)); }
                [Fact] public void F_ApplyR_4Args() { System.Func<int, int, int, int> add = (a, b, c) => a + b + c; var f = F.ApplyR<int, int, int, int>(add, 10); Assert.Equal(15, f(2, 3)); }

        // ===== F - UnitValue =====
        [Fact] public void F_UnitValue_IsUnit() { Assert.Equal(Unit.Default, F.UnitValue); }

        // ===== F - None =====
        [Fact] public void F_None_IsNoneType() { Assert.IsType<NoneType>(F.None); }
    }
}
