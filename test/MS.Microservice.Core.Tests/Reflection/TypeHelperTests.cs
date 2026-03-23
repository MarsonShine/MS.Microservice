using FluentAssertions;
using MS.Microservice.Core.Reflection;

namespace MS.Microservice.Core.Tests.Reflection
{
    public class TypeHelperTests
    {
        [Fact]
        public void GetDefaultValue_ValueType_ReturnsDefault()
        {
            TypeHelper.GetDefaultValue(typeof(int)).Should().Be(0);
            TypeHelper.GetDefaultValue(typeof(bool)).Should().Be(false);
            TypeHelper.GetDefaultValue(typeof(double)).Should().Be(0.0);
        }

        [Fact]
        public void GetDefaultValue_ReferenceType_ReturnsNull()
        {
            TypeHelper.GetDefaultValue(typeof(string)).Should().BeNull();
            TypeHelper.GetDefaultValue(typeof(object)).Should().BeNull();
        }        [Fact]
        public void IsDefaultValue_Null_ReturnsTrue()
        {
            TypeHelper.IsDefaultValue(null!).Should().BeTrue();
        }

        [Fact]
        public void IsDefaultValue_DefaultInt_ReturnsTrue()
        {
            TypeHelper.IsDefaultValue(0).Should().BeTrue();
        }

        [Fact]
        public void IsDefaultValue_NonDefaultInt_ReturnsFalse()
        {
            TypeHelper.IsDefaultValue(42).Should().BeFalse();
        }

        [Fact]
        public void GetGenericTypeName_GenericType_ReturnsFormattedName()
        {
            var name = TypeHelper.GetGenericTypeName(typeof(System.Collections.Generic.List<int>));
            name.Should().Be("List<Int32>");
        }

        [Fact]
        public void GetGenericTypeName_NonGenericType_ReturnsSimpleName()
        {
            var name = TypeHelper.GetGenericTypeName(typeof(string));
            name.Should().Be("String");
        }

        [Fact]
        public void GetGenericTypeName_FromObject_Works()
        {
            var list = new System.Collections.Generic.List<string>();
            var name = TypeHelper.GetGenericTypeName(list);
            name.Should().Be("List<String>");
        }

        [Fact]
        public void GetFullMethodName_ReturnsCorrectFullName()
        {
            var helper = new SampleClass();
            var name = TypeHelper.GetFullMethodName(helper, "DoWork");
            name.Should().Contain("SampleClass").And.Contain("DoWork");
        }

        private class SampleClass
        {
            public void DoWork() { }
        }
    }
}
