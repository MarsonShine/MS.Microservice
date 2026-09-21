using System;
using System.Collections.Generic;
using System.Reflection;
using MS.Microservice.Lab.AotExamples.Legacy.Reflection;
using Xunit;

namespace MS.Microservice.Lab.AotExamples.Tests.Reflection;

/// <summary>
/// 属性访问器的编译契约：顺序、过滤、装箱与读写委托在两个实现下必须一致。
/// 这边只锁定访问器本身；查询字符串的端到端同源对比见
/// <c>MS.Microservice.Lab.AotExamples.Tests.Reflection.QueryStringStrategyParityTests</c>。
/// </summary>
public sealed class PropertyAccessorsTests
{
    [Fact]
    public void BothStrategiesCompileIdenticalAccessorShapes()
    {
        foreach (var strategy in new[] { PropertyAccessors.Il, PropertyAccessors.Expression })
        {
            var accessors = strategy.CreateAccessors(typeof(Sample));
            Assert.Equal(["Id", "Name", "Tags", "InitOnly", "ReadOnly"], Array.ConvertAll(accessors, accessor => accessor.Name));

            foreach (var accessor in accessors) Assert.NotNull(accessor.GetValue);

            var sample = new Sample { Id = 7, Name = "alice", Tags = ["a", "b"] };
            Assert.Equal(7, accessors[0].GetValue!.Invoke(sample));
            Assert.Equal("alice", accessors[1].GetValue!.Invoke(sample));
            Assert.Equal(new[] { "a", "b" }, Assert.IsType<string[]>(accessors[2].GetValue!.Invoke(sample)));
            Assert.Equal("fixed", accessors[4].GetValue!.Invoke(sample));

            // 私有 setter、init 访问器与只读属性都不可写；两个实现必须给出同样的结论。
            Assert.NotNull(accessors[0].SetValue);
            accessors[0].SetValue!.Invoke(sample, 9);
            Assert.Equal(9, sample.Id);
            Assert.Null(accessors[3].SetValue);
            Assert.Null(accessors[4].SetValue);
        }
    }

    [Fact]
    public void IndexersStaticAndPrivatePropertiesAreExcluded()
    {
        foreach (var strategy in new[] { PropertyAccessors.Il, PropertyAccessors.Expression })
        {
            var accessors = strategy.CreateAccessors(typeof(Opaque));
            Assert.Empty(accessors);
        }
    }

    [Fact]
    public void FactoryFollowsConstructorAvailability()
    {
        foreach (var strategy in new[] { PropertyAccessors.Il, PropertyAccessors.Expression })
        {
            Assert.IsType<Sample>(strategy.CompileFactory(typeof(Sample))!());
            Assert.Null(strategy.CompileFactory(typeof(NoDefaultConstructor)));
        }
    }

    [Fact]
    public void CachesAreKeyedByStrategyAndType()
    {
        var previous = PropertyAccessors.Active;
        try
        {
            PropertyAccessors.Use(PropertyAccessors.Il);
            var il = PropertyAccessors.Get(typeof(Sample));
            Assert.Same(il, PropertyAccessors.Get(typeof(Sample)));

            // 切换实现后同一类型必须用新实现重新编译，而不是复用上一个实现的产物；
            // 切回来时原实现的缓存在，不应重新编译。
            PropertyAccessors.Use(PropertyAccessors.Expression);
            var expression = PropertyAccessors.Get(typeof(Sample));
            Assert.NotSame(il, expression);

            PropertyAccessors.Use(PropertyAccessors.Il);
            Assert.Same(il, PropertyAccessors.Get(typeof(Sample)));
        }
        finally
        {
            PropertyAccessors.Use(previous);
        }
    }

    private sealed class Sample
    {
        public int Id { get; set; }
        public string? Name { get; init; }
        public string[]? Tags { get; set; }
        public string? InitOnly { get; init; }
        public string ReadOnly => "fixed";
    }

    private sealed class NoDefaultConstructor(string value)
    {
        public string Value { get; } = value;
    }

    private sealed class Opaque
    {
        private int Hidden { get; set; }
        public static int Static { get; set; }
        public int this[int index] => index;
    }
}
