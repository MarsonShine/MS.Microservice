using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using MS.Microservice.Core.Net.Http;
using MS.Microservice.Core.Reflection;
using Xunit;

namespace MS.Microservice.Core.Tests.Net.Http;

/// <summary>
/// IL 与表达树两个实现必须逐字节同源：同一批查询对象、同一组断言，切换策略各跑一遍。
/// 这条测试抓到过两个只在一种实现里出现的缺陷（可枚举属性为 null 时直接枚举，以及数组的 ldlen 未判空）。
/// </summary>
public sealed class QueryStringStrategyParityTests
{
    private static readonly (string Name, object Body, string Expected)[] Cases =
    [
        ("scalar", new ScalarPayload { Id = 1, Name = "alice" }, "Id=1&Name=alice"),
        ("null-scalar", new ScalarPayload { Id = 2, Name = null }, "Id=2"),
        ("value-types", new ValuePayload { Amount = 1.25m, At = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc), Flag = true },
            "Amount=1.25&At=2024-01-02T03%3A04%3A05.0000000Z&Flag=True"),
        ("array", new CollectionPayload { Tags = ["a", "b"], Ids = [1, 2, 3] }, "Tags=a&Tags=b&Ids=1&Ids=2&Ids=3"),
        ("null-array", new CollectionPayload { Tags = null, Ids = null }, ""),
        ("empty-array", new CollectionPayload { Tags = [], Ids = [] }, ""),
        ("enum", new EnumPayload { Status = Status.Ready, Mode = Status.Ready }, "Status=Ready&Mode=Ready"),
        ("null-enum", new EnumPayload { Status = null, Mode = Status.Ready }, "Mode=Ready"),
        // 非空枚举走 IL 的专用 branch（不能像 Nullable<枚举> 那样先装箱再判空），必须单独覆盖。
        ("default-enum", new EnumPayload { Status = null, Mode = Status.None }, "Mode=None"),
        ("mixed-enum", new EnumPayload { Status = Status.Ready, Mode = Status.None }, "Status=Ready&Mode=None"),
        ("nested-collection", new CollectionPayload { Tags = [], Ids = [], Matrix = ["x"] }, "Matrix=x"),
        ("string-is-scalar", new ScalarPayload { Id = 3, Name = "a&b=c 中文" }, "Id=3&Name=a%26b%3Dc%20%E4%B8%AD%E6%96%87"),
        ("no-readers", new OpaquePayload(), ""),
    ];

    [Fact]
    public async Task BothStrategiesProduceIdenticalQueryStrings()
    {
        var previous = PropertyAccessors.Active;
        try
        {
            var results = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var (label, strategy) in new[] { ("IL", PropertyAccessors.Il), ("Expression", PropertyAccessors.Expression) })
            {
                // 缓存以（实现，类型）为键，切换策略会对同一类型重新编译，两个实现都真正跑到。
                PropertyAccessors.Use(strategy);
                var observed = new List<string>();
                foreach (var (_, body, _) in Cases)
                    observed.Add(await SendAsync(body));
                results[label] = [.. observed];
            }

            Assert.Equal(results["IL"], results["Expression"]);
            for (var i = 0; i < Cases.Length; i++)
                Assert.EndsWith(Cases[i].Expected, results["IL"][i], StringComparison.Ordinal);
        }
        finally
        {
            PropertyAccessors.Use(previous);
        }
    }

    [Fact]
    public void AccessorsExposeGettersSettersAndSkipNonWritableMembers()
    {
        var previous = PropertyAccessors.Active;
        try
        {
            foreach (var strategy in new[] { PropertyAccessors.Il, PropertyAccessors.Expression })
            {
                var accessors = strategy.CreateAccessors(typeof(WritablePayload));
                Assert.Equal(["ReadWrite", "InitOnly", "ReadOnly"], Array.ConvertAll(accessors, accessor => accessor.Name));

                var target = new WritablePayload();
                var readWrite = Array.Find(accessors, accessor => accessor.Name == "ReadWrite");
                readWrite.GetValue!.Invoke(target);
                readWrite.SetValue!.Invoke(target, "changed");
                Assert.Equal("changed", target.ReadWrite);

                // init 与只读属性不可写，反射能调但 IL 过不了校验，两个实现都必须一致地视为不可写。
                Assert.Null(Array.Find(accessors, accessor => accessor.Name == "InitOnly").SetValue);
                Assert.Null(Array.Find(accessors, accessor => accessor.Name == "ReadOnly").SetValue);
                Assert.NotNull(strategy.CompileFactory(typeof(WritablePayload)));
                // 只有带参构造函数的类型没有可用工厂。
                Assert.Null(strategy.CompileFactory(typeof(NoDefaultConstructorPayload)));
            }
        }
        finally
        {
            PropertyAccessors.Use(previous);
        }
    }

    private static async Task<string> SendAsync(object body)
    {
        var handler = new RecordingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        await new LogHttpClient(global::Microsoft.Extensions.Logging.Abstractions.NullLogger<LogHttpClient>.Instance, http)
            .GetAsync<object>("orders", body);
        return handler.Uri!;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string? Uri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
        {
            Uri = request.RequestUri!.AbsoluteUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { }) });
        }
    }

    private sealed class ScalarPayload
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    private sealed class ValuePayload
    {
        public decimal Amount { get; set; }
        public DateTime At { get; set; }
        public bool Flag { get; set; }
    }

    private sealed class CollectionPayload
    {
        public string[]? Tags { get; set; }
        public List<int>? Ids { get; set; }
        public string[]? Matrix { get; set; }
    }

    private sealed class EnumPayload
    {
        public Status? Status { get; set; }
        public Status Mode { get; set; }
    }

    private sealed class OpaquePayload
    {
        private int Hidden { get; set; }
        public int this[int index] => index;
    }

    private sealed class NoDefaultConstructorPayload(string value)
    {
        public string Value { get; } = value;
    }

    private sealed class WritablePayload
    {
        public string? ReadWrite { get; set; }
        public string? InitOnly { get; init; }
        public string ReadOnly => "fixed";
    }

    private enum Status
    {
        None = 0,
        Ready = 1
    }
}
