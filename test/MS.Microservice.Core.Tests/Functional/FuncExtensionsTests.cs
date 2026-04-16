using MS.Microservice.Core.Functional;

namespace MS.Microservice.Core.Tests.Functional
{
    public class FuncExtensionsTests
    {
        [Fact]
        public void Curry_WhenFunctionHasTwoArguments_ReturnsCurriedFunction()
        {
            Func<int, int, int> add = (left, right) => left + right;

            var curried = add.Curry();

            Assert.Equal(3, curried(1)(2));
        }

        [Fact]
        public void Apply_WhenFunctionHasThreeArguments_PartiallyAppliesArguments()
        {
            Func<int, int, int, int> add = (left, middle, right) => left + middle + right;

            var result = add.Apply(1).Apply(2)(3);

            Assert.Equal(6, result);
        }
    }
}
