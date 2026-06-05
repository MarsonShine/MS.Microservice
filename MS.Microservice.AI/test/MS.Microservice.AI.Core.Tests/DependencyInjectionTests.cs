using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core;

namespace MS.Microservice.AI.Core.Tests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void AddMicroserviceAI_ShouldRegisterRoutingClient_AndProviderPipeline()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:DefaultProvider"] = "OpenAI",
                ["AI:Providers:OpenAI:ApiKey"] = "test-key",
                ["AI:Providers:OpenAI:BaseAddress"] = "https://api.openai.com/v1/",
                ["AI:Providers:OpenAI:TimeoutSeconds"] = "10",
                ["AI:Providers:OpenAI:MaxRetryAttempts"] = "1",
                ["AI:Providers:OpenAI:ConcurrencyLimit"] = "2",
                ["AI:Models:Chat:Default:Provider"] = "OpenAI",
                ["AI:Models:Chat:Default:Model"] = "gpt-4.1-mini",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddMicroserviceAI(configuration)
            .AddOpenAI();

        using var provider = services.BuildServiceProvider();
        var chatClient = provider.GetRequiredService<IAIChatClient>();
        var providers = provider.GetRequiredService<IEnumerable<IAIChatProvider>>();
        var options = provider.GetRequiredService<IOptions<AIOptions>>().Value;

        chatClient.Should().BeOfType<RoutingAIChatClient>();
        providers.Select(item => item.Name).Should().ContainSingle(name => name == "OpenAI");
        options.DefaultProvider.Should().Be("OpenAI");
        options.Models.Chat.Should().ContainKey("Default");
    }
}