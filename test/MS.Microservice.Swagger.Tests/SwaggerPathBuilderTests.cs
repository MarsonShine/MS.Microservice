using FluentAssertions;
using MS.Microservice.Swagger;
using Xunit;

namespace MS.Microservice.Swagger.Tests;

public sealed class SwaggerPathBuilderTests
{
    [Theory]
    [InlineData(null, "swagger", "swagger/{documentName}/swagger.json", "/swagger/v2/swagger.json")]
    [InlineData("docs", "docs", "docs/{documentName}/swagger.json", "/docs/v2/swagger.json")]
    [InlineData("/docs/api/", "docs/api", "docs/api/{documentName}/swagger.json", "/docs/api/v2/swagger.json")]
    [InlineData("", "", "{documentName}/swagger.json", "/v2/swagger.json")]
    [InlineData("///", "swagger", "swagger/{documentName}/swagger.json", "/swagger/v2/swagger.json")]
    [InlineData("/  /", "swagger", "swagger/{documentName}/swagger.json", "/swagger/v2/swagger.json")]
    public void OnePrefixDeterminesTheUiRouteTemplateAndJsonEndpoint(
        string? configuredPrefix, string expectedUi, string expectedRoute, string expectedEndpoint)
    {
        var prefix = SwaggerPathBuilder.NormalizeRoutePrefix(configuredPrefix);

        prefix.Should().Be(expectedUi);
        SwaggerPathBuilder.BuildRouteTemplate(prefix).Should().Be(expectedRoute);
        SwaggerPathBuilder.BuildJsonEndpoint(prefix, "v2").Should().Be(expectedEndpoint);
    }
}
