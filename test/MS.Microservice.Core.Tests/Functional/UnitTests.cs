using MS.Microservice.Core.Functional;

namespace MS.Microservice.Core.Tests.Functional
{
    /// <summary>
    /// 针对 <see cref="Unit"/> 类型的单元测试。
    /// </summary>
    public class UnitTests
    {
        [Fact]
        public void Default_EqualToAnotherDefault()
        {
            Unit a = Unit.Default;
            Unit b = default;
            Assert.Equal(a, b);
        }

        [Fact]
        public void Equality_TwoUnits_AreAlwaysEqual()
        {
            var a = Unit.Default;
            var b = Unit.Default;
            Assert.True(a == b);
            Assert.False(a != b);
        }

        [Fact]
        public void ToString_Returns_EmptyParentheses()
        {
            Assert.Equal("()", Unit.Default.ToString());
        }

        [Fact]
        public void GetHashCode_IsSameForAllInstances()
        {
            Assert.Equal(Unit.Default.GetHashCode(), default(Unit).GetHashCode());
        }

        [Fact]
        public void F_UnitValue_ReturnsSameValueAsDefault()
        {
            // 通过工厂属性获取 Unit
            Unit fromFactory = F.UnitValue;
            Assert.Equal(Unit.Default, fromFactory);
        }

        [Fact]
        public void Action_CanBeWrappedAsFunc_ReturningUnit()
        {
            // 演示 Unit 允许把 Action 包装成 Func<Unit>
            var sideEffect = 0;
            Func<Unit> wrappedAction = () => { sideEffect++; return Unit.Default; };

            Unit result = wrappedAction();

            Assert.Equal(1, sideEffect);
            Assert.Equal(Unit.Default, result);
        }
    }
}
