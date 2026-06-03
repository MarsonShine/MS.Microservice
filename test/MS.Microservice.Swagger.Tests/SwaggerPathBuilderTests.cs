using FluentAssertions;
using MS.Microservice.Swagger;
using Xunit;

namespace MS.Microservice.Swagger.Tests;

public sealed class SwaggerPathBuilderTests
{
    [Fact]
    public void BuildJsonEndpoint_ShouldUseDefaultSwaggerPrefix_WhenRoutePrefixIsNull()
    {
        var endpoint = SwaggerPathBuilder.BuildJsonEndpoint(null, "v1");
        var routeTemplate = SwaggerPathBuilder.BuildRouteTemplate(null);
        var uiPrefix = SwaggerPathBuilder.NormalizeUiRoutePrefix(null);

        endpoint.Should().Be("/swagger/v1/swagger.json");
        routeTemplate.Should().Be("swagger/{documentName}/swagger.json");
        uiPrefix.Should().Be("swagger");
    }

    [Fact]
    public void BuildJsonEndpoint_ShouldUseCustomPrefix_WhenRoutePrefixIsProvided()
    {
        var endpoint = SwaggerPathBuilder.BuildJsonEndpoint("docs", "v2");
        var routeTemplate = SwaggerPathBuilder.BuildRouteTemplate("docs");
        var uiPrefix = SwaggerPathBuilder.NormalizeUiRoutePrefix("docs");

        endpoint.Should().Be("/docs/v2/swagger.json");
        routeTemplate.Should().Be("docs/{documentName}/swagger.json");
        uiPrefix.Should().Be("docs");
    }

    [Fact]
    public void BuildJsonEndpoint_ShouldServeAtRoot_WhenRoutePrefixIsEmptyString()
    {
        var endpoint = SwaggerPathBuilder.BuildJsonEndpoint(string.Empty, "v1");
        var routeTemplate = SwaggerPathBuilder.BuildRouteTemplate(string.Empty);
        var uiPrefix = SwaggerPathBuilder.NormalizeUiRoutePrefix(string.Empty);

        endpoint.Should().Be("/v1/swagger.json");
        routeTemplate.Should().Be("{documentName}/swagger.json");
        uiPrefix.Should().BeEmpty();
    }
}
