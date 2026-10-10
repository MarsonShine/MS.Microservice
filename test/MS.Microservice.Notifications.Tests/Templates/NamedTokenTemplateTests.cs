using MS.Microservice.Notifications.Templates;
using Xunit;

namespace MS.Microservice.Notifications.Tests.Templates;

public sealed class NamedTokenTemplateTests
{
    [Fact]
    public void Render_HandlesRepeatedNamedTokensUnicodeAndNullValues()
    {
        var values = new Dictionary<string, string?> { ["User.Name"] = "中文🙂", ["empty"] = null };

        Assert.Equal("Hello 中文🙂/中文🙂!", NamedTokenTemplate.Render("Hello {{User.Name}}/{{User.Name}}{{empty}}!", values));
        Assert.Equal("{plain JSON}", NamedTokenTemplate.Render("{plain JSON}", values));
        Assert.Empty(NamedTokenTemplate.Render("", values));
    }

    [Fact]
    public void Render_DoesNotRecursivelyInterpretReplacementText()
    {
        var values = new Dictionary<string, string?> { ["value"] = "{{secret}}", ["secret"] = "must not interpolate" };

        Assert.Equal("{{secret}}", NamedTokenTemplate.Render("{{value}}", values));
    }

    [Fact]
    public void Render_HtmlEncodingAppliesToValuesOnly()
    {
        var values = new Dictionary<string, string?> { ["name"] = "<script>\"&'{{hidden}}" };

        Assert.Equal("<p>&lt;script&gt;&quot;&amp;&#39;{{hidden}}</p>", NamedTokenTemplate.Render("<p>{{name}}</p>", values, true));
        Assert.Equal("<p><script>\"&'{{hidden}}</p>", NamedTokenTemplate.Render("<p>{{name}}</p>", values));
    }

    [Theory]
    [InlineData("{{missing}}")]
    [InlineData("{{NAME}}")]
    public void Render_MissingValuesFailExplicitly(string template)
    {
        Assert.Throws<KeyNotFoundException>(() => NamedTokenTemplate.Render(template, new Dictionary<string, string?> { ["name"] = "value" }));
    }

    [Theory]
    [InlineData("{{name")]
    [InlineData("name}}")]
    [InlineData("{{}}")]
    [InlineData("{{ name }}")]
    [InlineData("{{a..b}}")]
    [InlineData("{{a.}}")]
    [InlineData("{{.a}}")]
    [InlineData("{{0name}}")]
    [InlineData("{{name()}}")]
    [InlineData("{{{{name}}}}")]
    public void Render_RejectsMalformedTokensAndCodeSyntax(string template)
    {
        Assert.Throws<FormatException>(() => NamedTokenTemplate.Render(template, new Dictionary<string, string?>()));
    }
}
