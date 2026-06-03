using FluentAssertions;
using Microsoft.Extensions.Options;
using MS.Microservice.Swagger;
using Swashbuckle.AspNetCore.SwaggerGen;
using Xunit;

namespace MS.Microservice.Swagger.Tests;

public sealed class ConfigureSwaggerGenOptionsTests
{
    [Fact]
    public void Configure_ShouldAddSwaggerDocumentAndBearerSecurity_WhenEnabled()
    {
        var options = Options.Create(new SwaggerOptions
        {
            IsEnabled = true,
            EnabledSecurity = true,
            DocumentName = "v2",
            DocumentTitle = "Payments API",
            DocumentVersion = "v2",
        });
        var swaggerGenOptions = new SwaggerGenOptions();
        var configurator = new ConfigureSwaggerGenOptions(options);

        configurator.Configure(swaggerGenOptions);

        swaggerGenOptions.SwaggerGeneratorOptions.SwaggerDocs.Should().ContainKey("v2");
        swaggerGenOptions.SwaggerGeneratorOptions.SwaggerDocs["v2"].Title.Should().Be("Payments API");
        swaggerGenOptions.SwaggerGeneratorOptions.SwaggerDocs["v2"].Version.Should().Be("v2");
        swaggerGenOptions.SwaggerGeneratorOptions.SecuritySchemes.Should().ContainKey("bearer");
        swaggerGenOptions.SwaggerGeneratorOptions.SecurityRequirements.Should().HaveCount(1);
    }

    [Fact]
    public void Configure_ShouldDoNothing_WhenDisabled()
    {
        var options = Options.Create(new SwaggerOptions
        {
            IsEnabled = false,
        });
        var swaggerGenOptions = new SwaggerGenOptions();
        var configurator = new ConfigureSwaggerGenOptions(options);

        configurator.Configure(swaggerGenOptions);

        swaggerGenOptions.SwaggerGeneratorOptions.SwaggerDocs.Should().BeEmpty();
        swaggerGenOptions.SwaggerGeneratorOptions.SecuritySchemes.Should().BeEmpty();
    }

    [Fact]
    public void ResolveXmlCommentsPath_ShouldReturnExplicitAbsolutePath_WhenFileExists()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"swagger-{Guid.NewGuid():N}.xml");
        File.WriteAllText(tempFile, "<doc />");

        try
        {
            var path = ConfigureSwaggerGenOptions.ResolveXmlCommentsPath(new SwaggerOptions
            {
                SwaggerXmlFile = tempFile,
            });

            path.Should().Be(tempFile);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ResolveXmlCommentsPath_ShouldReturnNull_WhenExplicitFileDoesNotExist()
    {
        var path = ConfigureSwaggerGenOptions.ResolveXmlCommentsPath(new SwaggerOptions
        {
            SwaggerXmlFile = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.xml"),
        });

        path.Should().BeNull();
    }
}
