using MS.Microservice.Core.Functional;

namespace MS.Microservice.Core.Tests.Functional
{
    /// <summary>
    /// 针对 <see cref="Option{T}"/> 及其扩展方法的单元测试。
    /// 覆盖：创建、隐式转换、Match、Map、Bind、GetOrElse、OrElse、Where、ForEach、AsEnumerable、LINQ。
    /// </summary>
    public class OptionTests
    {
        // ── 创建与基本属性 ────────────────────────────────────────────────────────

        [Fact]
        public void Some_CreatesSomeOption_WithCorrectValue()
        {
            Option<int> opt = F.Some(42);
            Assert.True(opt.IsSome);
            Assert.False(opt.IsNone);
        }

        [Fact]
        public void None_CreatesNoneOption()
        {
            Option<int> opt = F.None;
            Assert.True(opt.IsNone);
            Assert.False(opt.IsSome);
        }

        [Fact]
        public void Some_ThrowsArgumentNullException_WhenValueIsNull()
        {
            // F.Some 不允许包含 null
            Assert.Throws<ArgumentNullException>(() => F.Some<string>(null!));
        }

        // ── 隐式转换 ──────────────────────────────────────────────────────────────

        [Fact]
        public void ImplicitConversion_FromNonNullValue_CreatesSome()
        {
            Option<string> opt = "hello";
            Assert.True(opt.IsSome);
        }

        [Fact]
        public void ImplicitConversion_FromNullValue_CreatesNone()
        {
            Option<string> opt = (string?)null;
            Assert.True(opt.IsNone);
        }

        [Fact]
        public void ImplicitConversion_FromNoneType_CreatesNone()
        {
            Option<int> opt = F.None;
            Assert.True(opt.IsNone);
        }

        [Fact]
        public void ImplicitConversion_FromSomeStruct_CreatesSome()
        {
            Option<int> opt = F.Some(10);
            Assert.True(opt.IsSome);
        }

        // ── 相等性 ────────────────────────────────────────────────────────────────

        [Fact]
        public void Equality_TwoSomeWithSameValue_AreEqual()
        {
            Option<int> a = F.Some(1);
            Option<int> b = F.Some(1);
            Assert.Equal(a, b);
            Assert.True(a == b);
        }

        [Fact]
        public void Equality_TwoSomeWithDifferentValues_AreNotEqual()
        {
            Option<int> a = F.Some(1);
            Option<int> b = F.Some(2);
            Assert.NotEqual(a, b);
            Assert.True(a != b);
        }

        [Fact]
        public void Equality_TwoNone_AreEqual()
        {
            Option<int> a = F.None;
            Option<int> b = F.None;
            Assert.Equal(a, b);
        }

        [Fact]
        public void Equality_SomeAndNone_AreNotEqual()
        {
            Option<int> some = F.Some(1);
            Option<int> none = F.None;
            Assert.NotEqual(some, none);
        }

        // ── ToString ─────────────────────────────────────────────────────────────

        [Fact]
        public void ToString_Some_ContainsSomePrefix()
        {
            Option<int> opt = F.Some(42);
            Assert.Equal("Some(42)", opt.ToString());
        }

        [Fact]
        public void ToString_None_ReturnsNone()
        {
            Option<int> opt = F.None;
            Assert.Equal("None", opt.ToString());
        }

        // ── Match ─────────────────────────────────────────────────────────────────

        [Fact]
        public void Match_Some_ExecutesSomeBranch()
        {
            Option<int> opt = F.Some(5);
            int result = opt.Match(none: () => 0, some: v => v * 2);
            Assert.Equal(10, result);
        }

        [Fact]
        public void Match_None_ExecutesNoneBranch()
        {
            Option<int> opt = F.None;
            int result = opt.Match(none: () => -1, some: v => v);
            Assert.Equal(-1, result);
        }

        [Fact]
        public void Match_WithValueOverload_Some_ExecutesSomeBranch()
        {
            Option<int> opt = F.Some(5);
            string result = opt.Match(none: "empty", some: v => $"value:{v}");
            Assert.Equal("value:5", result);
        }

        [Fact]
        public void Match_WithValueOverload_None_ReturnsNoneValue()
        {
            Option<int> opt = F.None;
            string result = opt.Match(none: "empty", some: v => $"value:{v}");
            Assert.Equal("empty", result);
        }

        // ── Map ──────────────────────────────────────────────────────────────────

        [Fact]
        public void Map_Some_TransformsValue()
        {
            Option<int> opt = F.Some(3);
            Option<int> result = opt.Map(x => x * 2);
            Assert.Equal(F.Some(6), result);
        }

        [Fact]
        public void Map_None_ReturnsNone_WithoutInvokingF()
        {
            Option<int> opt = F.None;
            bool wasCalled = false;
            Func<int, int> doubler = x => { wasCalled = true; return x * 2; };
            Option<int> result = opt.Map(doubler);
            Assert.True(result.IsNone);
            Assert.False(wasCalled);
        }

        [Fact]
        public void Map_ChainedMaps_TransformsCorrectly()
        {
            Option<int> maybeInt = F.Some(5);
            Option<string> result = maybeInt
                .Map(x => x * 2)          // Some(10)
                .Map(x => x.ToString());   // Some("10")
            Assert.Equal(F.Some("10"), result);
        }

        // ── Bind ─────────────────────────────────────────────────────────────────

        [Fact]
        public void Bind_Some_WithFunctionReturningSome_ReturnsSome()
        {
            Option<int> opt = F.Some(5);
            Option<int> result = opt.Bind(x => (Option<int>)F.Some(x + 1));
            Assert.Equal(F.Some(6), result);
        }

        [Fact]
        public void Bind_Some_WithFunctionReturningNone_ReturnsNone()
        {
            Option<int> opt = F.Some(5);
            Option<int> noneInt = F.None;
            Option<int> result = opt.Bind(_ => noneInt);
            Assert.True(result.IsNone);
        }

        [Fact]
        public void Bind_None_ReturnsNone_WithoutInvokingF()
        {
            Option<int> opt = F.None;
            bool wasCalled = false;
            Func<int, Option<int>> fn = x => { wasCalled = true; return F.Some(x); };
            Option<int> result = opt.Bind(fn);
            Assert.True(result.IsNone);
            Assert.False(wasCalled);
        }

        [Fact]
        public void Bind_CanChainMultipleOperations()
        {
            // 模拟：解析字符串 → 验证正数
            static Option<int> ParsePositiveInt(string s)
            {
                Option<int> none = F.None;
                return int.TryParse(s, out int n) ? F.Some(n) : none;
            }

            static Option<int> EnsurePositive(int n)
            {
                Option<int> none = F.None;
                return n > 0 ? F.Some(n) : none;
            }

            Option<int> result = ((Option<string>)F.Some("42"))
                .Bind(ParsePositiveInt)
                .Bind(EnsurePositive);

            Assert.Equal(F.Some(42), result);
        }

        [Fact]
        public void Bind_ShortCircuitsOnNone()
        {
            static Option<int> ParsePositiveInt(string s)
            {
                Option<int> none = F.None;
                return int.TryParse(s, out int n) ? F.Some(n) : none;
            }

            static Option<int> EnsurePositive(int n)
            {
                Option<int> none = F.None;
                return n > 0 ? F.Some(n) : none;
            }

            // "-5" 解析成功但不满足 EnsurePositive，链路短路
            Option<int> result = ((Option<string>)F.Some("-5"))
                .Bind(ParsePositiveInt)
                .Bind(EnsurePositive);

            Assert.True(result.IsNone);
        }

        // ── GetOrElse ─────────────────────────────────────────────────────────────

        [Fact]
        public void GetOrElse_Some_ReturnsInternalValue()
        {
            Option<int> opt = F.Some(99);
            Assert.Equal(99, opt.GetOrElse(0));
        }

        [Fact]
        public void GetOrElse_None_ReturnsDefaultValue()
        {
            Option<int> opt = F.None;
            Assert.Equal(0, opt.GetOrElse(0));
        }

        [Fact]
        public void GetOrElse_WithFallbackFunc_None_InvokesFallback()
        {
            Option<int> opt = F.None;
            bool fallbackCalled = false;
            int result = opt.GetOrElse(() => { fallbackCalled = true; return -1; });
            Assert.Equal(-1, result);
            Assert.True(fallbackCalled);
        }

        [Fact]
        public void GetOrElse_WithFallbackFunc_Some_DoesNotInvokeFallback()
        {
            Option<int> opt = F.Some(7);
            bool fallbackCalled = false;
            int result = opt.GetOrElse(() => { fallbackCalled = true; return -1; });
            Assert.Equal(7, result);
            Assert.False(fallbackCalled);
        }

        // ── OrElse ────────────────────────────────────────────────────────────────

        [Fact]
        public void OrElse_Some_ReturnsOriginalOption()
        {
            Option<int> opt = F.Some(1);
            Option<int> fallback = F.Some(99);
            Assert.Equal(opt, opt.OrElse(fallback));
        }

        [Fact]
        public void OrElse_None_ReturnsFallbackOption()
        {
            Option<int> opt = F.None;
            Option<int> fallback = F.Some(99);
            Assert.Equal(fallback, opt.OrElse(fallback));
        }

        [Fact]
        public void OrElse_WithFallbackFunc_None_InvokesFallback()
        {
            Option<int> opt = F.None;
            bool called = false;
            Option<int> result = opt.OrElse(() => { called = true; return F.Some(5); });
            Assert.True(called);
            Assert.Equal(F.Some(5), result);
        }

        // ── Where ─────────────────────────────────────────────────────────────────

        [Fact]
        public void Where_Some_PredicateTrue_ReturnsSome()
        {
            Option<int> opt = F.Some(10);
            Option<int> result = opt.Where(x => x > 5);
            Assert.True(result.IsSome);
        }

        [Fact]
        public void Where_Some_PredicateFalse_ReturnsNone()
        {
            Option<int> opt = F.Some(3);
            Option<int> result = opt.Where(x => x > 5);
            Assert.True(result.IsNone);
        }

        [Fact]
        public void Where_None_AlwaysReturnsNone()
        {
            Option<int> opt = F.None;
            bool wasCalled = false;
            Option<int> result = opt.Where(x => { wasCalled = true; return true; });
            Assert.True(result.IsNone);
            Assert.False(wasCalled);
        }

        // ── ForEach ───────────────────────────────────────────────────────────────

        [Fact]
        public void ForEach_Some_ExecutesAction()
        {
            Option<int> opt = F.Some(5);
            int captured = 0;
            opt.ForEach(v => captured = v);
            Assert.Equal(5, captured);
        }

        [Fact]
        public void ForEach_None_DoesNotExecuteAction()
        {
            Option<int> opt = F.None;
            bool wasCalled = false;
            opt.ForEach(_ => wasCalled = true);
            Assert.False(wasCalled);
        }

        [Fact]
        public void ForEach_ReturnsUnit()
        {
            Option<int> opt = F.Some(1);
            Unit result = opt.ForEach(_ => { });
            Assert.Equal(Unit.Default, result);
        }

        // ── AsEnumerable ──────────────────────────────────────────────────────────

        [Fact]
        public void AsEnumerable_Some_ReturnsSingleElementSequence()
        {
            Option<int> opt = F.Some(42);
            var seq = opt.AsEnumerable().ToList();
            Assert.Single(seq);
            Assert.Equal(42, seq[0]);
        }

        [Fact]
        public void AsEnumerable_None_ReturnsEmptySequence()
        {
            Option<int> opt = F.None;
            Assert.Empty(opt.AsEnumerable());
        }

        // ── LINQ 查询语法 ─────────────────────────────────────────────────────────

        [Fact]
        public void LinqSelect_Some_TransformsValue()
        {
            Option<int> maybeX = F.Some(10);
            Option<int> result =
                from x in maybeX
                select x * 3;

            Assert.Equal(F.Some(30), result);
        }

        [Fact]
        public void LinqSelect_None_ReturnsNone()
        {
            Option<int> none = F.None;
            Option<int> result =
                from x in none
                select x * 3;

            Assert.True(result.IsNone);
        }

        [Fact]
        public void LinqSelectMany_BothSome_ReturnsProjectedSome()
        {
            Option<int> maybeAge = F.Some(30);
            Option<string> maybeName = F.Some("Alice");

            Option<string> result =
                from age in maybeAge
                from name in maybeName
                select $"{name} is {age}";

            Assert.Equal(F.Some("Alice is 30"), result);
        }

        [Fact]
        public void LinqSelectMany_FirstNone_ReturnsNone()
        {
            Option<int> maybeAge = F.None;
            Option<string> maybeName = F.Some("Alice");

            Option<string> result =
                from age in maybeAge
                from name in maybeName
                select $"{name} is {age}";

            Assert.True(result.IsNone);
        }

        [Fact]
        public void LinqSelectMany_SecondNone_ReturnsNone()
        {
            Option<int> maybeAge = F.Some(30);
            Option<string> maybeName = F.None;

            Option<string> result =
                from age in maybeAge
                from name in maybeName
                select $"{name} is {age}";

            Assert.True(result.IsNone);
        }
    }
}
