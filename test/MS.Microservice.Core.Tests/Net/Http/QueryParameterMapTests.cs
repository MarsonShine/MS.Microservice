using System.Globalization;
using MS.Microservice.Core.Net.Http;

namespace MS.Microservice.Core.Tests.Net.Http;

public sealed class QueryParameterMapTests
{
    private static readonly QueryParameterMap<Query> Map = new(
        ("label", static value => value.Name), ("amount", static value => value.Amount),
        ("at", static value => value.At), ("status", static value => value.State),
        ("items", static value => value.Items));

    [Fact]
    public void ExplicitFields_PreserveOrderEscapingCollectionsAndInvariantFormatting()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new("fr-FR");
        try
        {
            var parts = new List<string> { "existing=1" };
            var value = new Query { Name = "中文 &", Amount = 1.25m,
                At = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero), State = DayOfWeek.Monday,
                Items = [1, new object?[] { "a b", null }, 2] };
            Assert.Equal(8, QueryStringParameters.Dispatch(value, parts, Map));
            Assert.Equal(new[] { "existing=1", "label=%E4%B8%AD%E6%96%87%20%26", "amount=1.25",
                "at=2024-01-02T03%3A04%3A05.0000000%2B00%3A00", "status=Monday", "items=1", "items=a%20b", "items=2" }, parts);
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    [Fact]
    public void NullAndEmptyValues_AreSkippedWhileFreshValuesAreReadEachTime()
    {
        var value = new Query { Items = [] };
        var parts = new List<string>();
        Assert.Equal(0, Map.Append(null, parts));
        Assert.Equal(0, Map.Append(value, parts));
        value.Name = "first";
        Map.Append(value, parts);
        value.Name = "";
        Map.Append(value, parts);
        Assert.Equal(new[] { "label=first", "label=" }, parts);
    }

    [Fact]
    public void MapOwnsItsDefinitionAndReadsOnlyExplicitFields()
    {
        (string Name, Func<Query, object?> Read)[] fields = [("chosen", static value => value.Name)];
        var map = new QueryParameterMap<Query>(fields);
        fields[0] = ("changed", static _ => throw new InvalidOperationException());
        var parts = new List<string>();
        map.Append(new Query { Name = "kept" }, parts);
        Assert.Equal(new[] { "chosen=kept" }, parts);
    }

    private sealed class Query
    {
        public string? Name { get; set; }
        public decimal? Amount { get; set; }
        public DateTimeOffset? At { get; set; }
        public DayOfWeek? State { get; set; }
        public object?[]? Items { get; set; }
        public string Unselected => throw new InvalidOperationException("Unselected getter must not run.");
    }
}
