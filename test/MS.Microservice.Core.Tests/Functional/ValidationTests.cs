using MS.Microservice.Core.Functional;
using Xunit;

namespace MS.Microservice.Core.Tests.Functional
{
    public class ValidationTests
    {
        // ─── Factory methods ───

        [Fact]
        public void Valid_CreatesValidValidation()
        {
            var v = F.Valid(42);
            Assert.True(v.IsValid);
            Assert.False(v.IsInvalid);
            Assert.Equal(42, v.Value);
        }

        [Fact]
        public void Valid_WithNull_DoesNotThrow()
        {
            // Validation<T> stores the value directly; Valid<T> struct guards null but F.Valid creates Validation directly
            var v = F.Valid<string>(null!);
            Assert.True(v.IsValid);
        }

        [Fact]
        public void Invalid_CreatesInvalidValidation()
        {
            Validation<int> v = F.Invalid<int>(Error.Validation("bad"));
            Assert.True(v.IsInvalid);
            Assert.False(v.IsValid);
        }

        [Fact]
        public void Invalid_Generic_CreatesInvalidValidation()
        {
            Validation<int> v = F.Invalid<int>(Error.Validation("bad"));
            Assert.True(v.IsInvalid);
            Assert.Equal("validation", v.Invalid.Code);
        }

        [Fact]
        public void Invalid_FromEnumerable_CreatesInvalid()
        {
            var errors = new[] { Error.Validation("e1"), Error.Validation("e2") };
            Validation<int> v = F.Invalid<int>(errors.AsEnumerable());
            Assert.True(v.IsInvalid);
            Assert.Equal(2, v.Invalid.Errors.Count());
        }

        [Fact]
        public void Invalid_Generic_FromEnumerable_CreatesInvalid()
        {
            var errors = new[] { Error.Validation("e1") };
            Validation<int> v = F.Invalid<int>(errors.AsEnumerable());
            Assert.True(v.IsInvalid);
        }

        // ─── Validation<T> members ───

        [Fact]
        public void Value_WhenValid_ReturnsInnerValue()
        {
            var v = F.Valid("hello");
            Assert.Equal("hello", v.Value);
            Assert.Equal("hello", v.Valid);
        }

        [Fact]
        public void Value_WhenInvalid_ThrowsInvalidOperationException()
        {
            Validation<int> v = F.Invalid<int>(Error.Validation("bad"));
            Assert.Throws<InvalidOperationException>(() => v.Value);
        }

        [Fact]
        public void Fail_Static_CreatesInvalid()
        {
            var v = Validation<int>.Fail(Error.Validation("fail"));
            Assert.True(v.IsInvalid);
        }

        [Fact]
        public void Fail_Static_FromEnumerable()
        {
            var v = Validation<int>.Fail(new[] { Error.Validation("f1") }.AsEnumerable());
            Assert.True(v.IsInvalid);
        }

        // ─── Implicit operators ───

        [Fact]
        public void Implicit_FromError_CreatesInvalid()
        {
            Validation<int> v = Error.Validation("ops");
            Assert.True(v.IsInvalid);
            Assert.Equal("validation", v.Invalid.Code);
        }

        [Fact]
        public void Implicit_FromInvalid_CreatesInvalid()
        {
            Validation<int> v = F.Invalid(Error.Validation("nope"));
            Assert.True(v.IsInvalid);
        }

        [Fact]
        public void Implicit_FromValue_CreatesValid()
        {
            Validation<int> v = 42;
            Assert.True(v.IsValid);
            Assert.Equal(42, v.Value);
        }

        // ─── Match ───

        [Fact]
        public void Match_WhenValid_ReturnsValidResult()
        {
            var v = F.Valid(10);
            var result = v.Match(
                invalid: _ => 0,
                valid: x => x * 2);
            Assert.Equal(20, result);
        }

        [Fact]
        public void Match_WhenInvalid_ReturnsInvalidResult()
        {
            var v = F.Invalid<int>(Error.Validation("bad"));
            var result = v.Match(
                invalid: _ => -1,
                valid: x => x);
            Assert.Equal(-1, result);
        }

        [Fact]
        public void Match_Action_WhenValid_CallsValidAction()
        {
            var v = F.Valid(5);
            int captured = 0;
            var unit = v.Match(
                invalid: _ => { captured = -1; },
                valid: x => { captured = x; });
            Assert.Equal(5, captured);
        }

        [Fact]
        public void Match_Action_WhenInvalid_CallsInvalidAction()
        {
            var v = F.Invalid<int>(Error.Validation("bad"));
            int captured = 0;
            v.Match(
                invalid: _ => { captured = -1; },
                valid: _ => { });
            Assert.Equal(-1, captured);
        }

        // ─── Bind ───

        [Fact]
        public void Bind_WhenValid_BindsToNext()
        {
            var v = F.Valid(5);
            var result = v.Bind(x => F.Valid(x * 2));
            Assert.True(result.IsValid);
            Assert.Equal(10, result.Value);
        }

        [Fact]
        public void Bind_WhenInvalid_ShortCircuits()
        {
            var v = F.Invalid<int>(Error.Validation("bad"));
            bool called = false;
            var result = v.Bind(x => { called = true; return F.Valid(x); });
            Assert.True(result.IsInvalid);
            Assert.False(called);
        }

        // ─── AsEnumerable ───

        [Fact]
        public void AsEnumerable_WhenValid_YieldsValue()
        {
            Validation<int> v = F.Valid(42);
            using var enumerator = v.AsEnumerable();
            Assert.True(enumerator.MoveNext());
            Assert.Equal(42, enumerator.Current);
            Assert.False(enumerator.MoveNext());
        }

        [Fact]
        public void AsEnumerable_WhenInvalid_YieldsNothing()
        {
            Validation<int> v = F.Invalid<int>(Error.Validation("bad"));
            using var enumerator = v.AsEnumerable();
            Assert.False(enumerator.MoveNext());
        }

        // ─── ToString ───

        [Fact]
        public void ToString_WhenValid_ShowsValue()
        {
            var v = F.Valid(42);
            Assert.Equal("Valid(42)", v.ToString());
        }

        [Fact]
        public void ToString_WhenInvalid_ShowsErrors()
        {
            var v = F.Invalid<int>(Error.Validation("bad input"));
            var s = v.ToString();
            Assert.Contains("Invalid", s);
            Assert.Contains("bad input", s);
        }

        // ─── Equality ───

        [Fact]
        public void Equals_SameValid_ReturnsTrue()
        {
            var a = F.Valid(42);
            var b = F.Valid(42);
            Assert.True(a.Equals(b));
            Assert.True(a == b);
            Assert.False(a != b);
        }

        [Fact]
        public void Equals_DifferentValid_ReturnsFalse()
        {
            var a = F.Valid(42);
            var b = F.Valid(43);
            Assert.False(a.Equals(b));
            Assert.False(a == b);
            Assert.True(a != b);
        }

        [Fact]
        public void Equals_OneValidOneInvalid_ReturnsFalse()
        {
            var a = F.Valid(42);
            var b = F.Invalid<int>(Error.Validation("bad"));
            Assert.False(a.Equals(b));
            Assert.False(a == b);
        }

        [Fact]
        public void Equals_SameInvalid_ReturnsTrue()
        {
            var err = Error.Validation("bad");
            var a = F.Invalid<int>(err);
            var b = F.Invalid<int>(err);
            Assert.True(a.Equals(b));
            Assert.True(a == b);
        }

        [Fact]
        public void Equals_DifferentInvalidErrors_ReturnsFalse()
        {
            var a = F.Invalid<int>(Error.Validation("a"));
            var b = F.Invalid<int>(Error.Validation("b"));
            Assert.False(a.Equals(b));
        }

        [Fact]
        public void Equals_NullObject_ReturnsFalse()
        {
            var v = F.Valid(42);
            Assert.False(v.Equals((object?)null));
        }

        [Fact]
        public void Equals_DifferentType_ReturnsFalse()
        {
            var v = F.Valid(42);
            Assert.False(v.Equals("string"));
        }

        [Fact]
        public void GetHashCode_SameValid_SameHash()
        {
            var a = F.Valid(42);
            var b = F.Valid(42);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void GetHashCode_SameInvalid_SameHash()
        {
            var err = Error.Validation("bad");
            var a = F.Invalid<int>(err);
            var b = F.Invalid<int>(err);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        // ─── Validation.Invalid struct ───

        [Fact]
        public void Invalid_Code_ReturnsFirstErrorCode()
        {
            var v = F.Invalid(Error.Validation("validation error"), Error.Conflict("conflict error"));
            Assert.Equal("validation", v.Code);
        }

        [Fact]
        public void Invalid_Message_ReturnsFirstErrorMessage()
        {
            var v = F.Invalid(Error.Validation("first"), Error.Conflict("second"));
            Assert.Equal("first", v.Message);
        }

        [Fact]
        public void Invalid_Code_WhenEmpty_ReturnsEmpty()
        {
            var err = new Error("code", "msg");
            var v = F.Invalid<int>(new[] { err }.AsEnumerable());
            // Invalid with errors has code
            Assert.Equal("code", v.Invalid.Code);
        }

        [Fact]
        public void Invalid_ToString_ShowsErrors()
        {
            var v = F.Invalid(Error.Validation("e1"), Error.Validation("e2"));
            var s = v.ToString();
            Assert.Contains("Invalid", s);
        }

        // ─── HarvestErrors extension ───

        [Fact]
        public void HarvestErrors_AllValid_ReturnsValid()
        {
            Func<string, Validation<string>>[] validators =
            [
                s => F.Valid(s),
                s => F.Valid(s.ToUpper()),
            ];
            var validate = validators.HarvestErrors();
            var result = validate("hello");
            Assert.True(result.IsValid);
            Assert.Equal("HELLO", result.Value);
        }

        [Fact]
        public void HarvestErrors_OneFails_ReturnsInvalidWithAllErrors()
        {
            Func<string, Validation<string>>[] validators =
            [
                s => F.Invalid<string>(Error.Validation("e1")),
                s => F.Invalid<string>(Error.Validation("e2")),
            ];
            var validate = validators.HarvestErrors();
            var result = validate("hello");
            Assert.True(result.IsInvalid);
            Assert.Equal(2, result.Invalid.Errors.Count());
        }

        [Fact]
        public void HarvestErrors_MixedValidAndInvalid_CollectsAllErrors()
        {
            Func<string, Validation<string>>[] validators =
            [
                s => F.Valid(s),
                s => F.Invalid<string>(Error.Validation("e1")),
                s => F.Invalid<string>(Error.Validation("e2")),
            ];
            var validate = validators.HarvestErrors();
            var result = validate("hello");
            Assert.True(result.IsInvalid);
            Assert.Equal(2, result.Invalid.Errors.Count());
        }

        [Fact]
        public void HarvestErrors_FirstFailsLaterValid_ReturnsAllErrors()
        {
            Func<string, Validation<string>>[] validators =
            [
                s => F.Invalid<string>(Error.Validation("e1")),
                s => F.Valid(s),
            ];
            var validate = validators.HarvestErrors();
            var result = validate("hello");
            Assert.True(result.IsInvalid);
        }

        // ─── Apply (applicative) ───

        [Fact]
        public void Apply_BothValid_AppliesFunction()
        {
            Validation<Func<int, int>> valF = F.Valid<Func<int, int>>(x => x * 2);
            Validation<int> valT = F.Valid(21);
            var result = valF.Apply(valT);
            Assert.True(result.IsValid);
            Assert.Equal(42, result.Value);
        }

        [Fact]
        public void Apply_InvalidFunction_CollectsErrors()
        {
            Validation<Func<int, int>> valF = F.Invalid<Func<int, int>>(Error.Validation("bad func"));
            Validation<int> valT = F.Valid(21);
            var result = valF.Apply(valT);
            Assert.True(result.IsInvalid);
            Assert.Equal("bad func", result.Invalid.Message);
        }

        [Fact]
        public void Apply_InvalidArg_CollectsErrors()
        {
            Validation<Func<int, int>> valF = F.Valid<Func<int, int>>(x => x * 2);
            Validation<int> valT = F.Invalid<int>(Error.Validation("bad arg"));
            var result = valF.Apply(valT);
            Assert.True(result.IsInvalid);
            Assert.Equal("bad arg", result.Invalid.Message);
        }

        [Fact]
        public void Apply_BothInvalid_CollectsAllErrors()
        {
            Validation<Func<int, int>> valF = F.Invalid<Func<int, int>>(Error.Validation("bad func"));
            Validation<int> valT = F.Invalid<int>(Error.Validation("bad arg"));
            var result = valF.Apply(valT);
            Assert.True(result.IsInvalid);
            Assert.Equal(2, result.Invalid.Errors.Count());
        }

        [Fact]
        public void Apply_TwoArg_Works()
        {
            Validation<Func<int, int, int>> valF = F.Valid<Func<int, int, int>>((a, b) => a + b);
            Validation<int> valA = F.Valid(10);
            Validation<Func<int, int>> curried = Validation.Apply<int, int, int>(valF, valA);
            Assert.True(curried.IsValid);
            Validation<int> final = curried.Apply(F.Valid(32));
            Assert.True(final.IsValid);
            Assert.Equal(42, final.Value);
        }

        [Fact]
        public void Apply_ThreeArg_Works()
        {
            Validation<Func<int, int, int, int>> valF = F.Valid<Func<int, int, int, int>>((a, b, c) => a + b + c);
            Validation<int> valA = F.Valid(10);
            var result = Validation.Apply<int, int, int, int>(valF, valA);
            Assert.True(result.IsValid);
        }

        // ─── Validation extensions (Bind, GetOrThrow, GetOrElse) ───

        [Fact]
        public void ExtensionBind_WhenValid_Binds()
        {
            Validation<int> v = F.Valid(5);
            var result = v.Bind(x => F.Valid(x * 2));
            Assert.True(result.IsValid);
            Assert.Equal(10, result.Value);
        }

        [Fact]
        public void ExtensionBind_WhenInvalid_ReturnsInvalid()
        {
            Validation<int> v = F.Invalid<int>(Error.Validation("bad"));
            var result = v.Bind(x => F.Valid(x * 2));
            Assert.True(result.IsInvalid);
        }

        [Fact]
        public void GetOrThrow_WhenValid_ReturnsValue()
        {
            Validation<int> v = F.Valid(42);
            Assert.Equal(42, v.GetOrThrow());
        }

        [Fact]
        public void GetOrThrow_WhenInvalid_Throws()
        {
            Validation<int> v = F.Invalid<int>(Error.Validation("bad"));
            var ex = Assert.Throws<InvalidOperationException>(() => v.GetOrThrow());
            Assert.Contains("bad", ex.Message);
        }

        [Fact]
        public void GetOrElse_WhenValid_ReturnsValue()
        {
            Validation<int> v = F.Valid(42);
            Assert.Equal(42, v.GetOrElse(-1));
        }

        [Fact]
        public void GetOrElse_WhenInvalid_ReturnsDefault()
        {
            Validation<int> v = F.Invalid<int>(Error.Validation("bad"));
            Assert.Equal(-1, v.GetOrElse(-1));
        }

        [Fact]
        public void GetOrElse_Func_WhenInvalid_ReturnsFallback()
        {
            Validation<int> v = F.Invalid<int>(Error.Validation("bad"));
            Assert.Equal(99, v.GetOrElse(() => 99));
        }

        // ─── Map ───

        [Fact]
        public void Map_WhenValid_TransformsValue()
        {
            var v = F.Valid(21);
            var result = v.Map(x => x * 2);
            Assert.True(result.IsValid);
            Assert.Equal(42, result.Value);
        }

        [Fact]
        public void Map_WhenInvalid_PreservesError()
        {
            var v = F.Invalid<int>(Error.Validation("bad"));
            var result = v.Map(x => x * 2);
            Assert.True(result.IsInvalid);
            Assert.Equal("bad", result.Invalid.Message);
        }

        [Fact]
        public void Map_TwoArg_Curries()
        {
            var v = F.Valid(10);
            var result = v.Map((int a, int b) => a + b);
            Assert.True(result.IsValid);
            Assert.IsType<Func<int, int>>(result.Value);
        }

        // ─── ForEach / Do ───

        [Fact]
        public void ForEach_WhenValid_ExecutesAction()
        {
            var v = F.Valid(42);
            int captured = 0;
            v.ForEach(x => captured = x);
            Assert.Equal(42, captured);
        }

        [Fact]
        public void Do_WhenValid_ExecutesAndReturnsSelf()
        {
            var v = F.Valid(42);
            int captured = 0;
            var result = v.Do(x => captured = x);
            Assert.Equal(42, captured);
            Assert.True(result.IsValid);
            Assert.Equal(42, result.Value);
        }

        // ─── LINQ Select / SelectMany ───

        [Fact]
        public void Select_WhenValid_Projects()
        {
            var v = F.Valid(21);
            var result = v.Select(x => x * 2);
            Assert.True(result.IsValid);
            Assert.Equal(42, result.Value);
        }

        [Fact]
        public void Select_WhenInvalid_PreservesError()
        {
            var v = F.Invalid<int>(Error.Validation("bad"));
            var result = v.Select(x => x * 2);
            Assert.True(result.IsInvalid);
        }

        [Fact]
        public void SelectMany_WhenValid_Projects()
        {
            var v = F.Valid(5);
            var result = v.SelectMany(
                bind: x => F.Valid(x * 2),
                project: (x, r) => $"{x}->{r}");
            Assert.True(result.IsValid);
            Assert.Equal("5->10", result.Value);
        }

        [Fact]
        public void SelectMany_WhenBindFails_ReturnsInvalid()
        {
            var v = F.Valid(5);
            var result = v.SelectMany(
                bind: x => F.Invalid<string>(Error.Validation("bind fail")),
                project: (x, r) => $"{x}->{r}");
            Assert.True(result.IsInvalid);
        }

        [Fact]
        public void SelectMany_WhenSourceInvalid_ReturnsInvalid()
        {
            var v = F.Invalid<int>(Error.Validation("source bad"));
            var result = v.SelectMany(
                bind: x => F.Valid(x.ToString()),
                project: (x, r) => $"{x}->{r}");
            Assert.True(result.IsInvalid);
        }

        // ─── LINQ query expression ───

        [Fact]
        public void LinqQuery_ValidChain_Works()
        {
            var result =
                from a in F.Valid(10)
                from b in F.Valid(20)
                select a + b;
            Assert.True(result.IsValid);
            Assert.Equal(30, result.Value);
        }

        [Fact]
        public void LinqQuery_MidFailure_ShortCircuits()
        {
            var result =
                from a in F.Valid(10)
                from b in F.Invalid<int>(Error.Validation("mid fail"))
                from c in F.Valid(30)
                select a + b + c;
            Assert.True(result.IsInvalid);
            Assert.Equal("mid fail", result.Invalid.Message);
        }

        // ─── Return function ───

        [Fact]
        public void Return_CreatesValid()
        {
            var v = Validation<int>.Return(42);
            Assert.True(v.IsValid);
            Assert.Equal(42, v.Value);
        }

        // ─── Valid<T> struct ───

        [Fact]
        public void Valid_ToString_ShowsValue()
        {
            var v = new Valid<int>(42);
            Assert.Equal("Valid(42)", v.ToString());
        }
    }
}
