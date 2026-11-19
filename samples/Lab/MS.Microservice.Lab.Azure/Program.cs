using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.Lab.Azure;

var assistantMode = args is ["--assistant", ..];
var outputArgs = assistantMode ? args[1..] : args;
if (outputArgs.Length > 1 || outputArgs is ["-h" or "--help"])
{
    Console.WriteLine("Usage: dotnet run --project samples/Lab/MS.Microservice.Lab.Azure -c Release -- [--assistant] [output-parent-directory]");
    return outputArgs.Length > 1 ? 2 : 0;
}

var configurationBuilder = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json");
if (assistantMode)
{
    configurationBuilder.AddJsonFile("appsettings.Assistant.json");
}

var configuration = configurationBuilder.AddEnvironmentVariables().Build();

var secretNames = new Dictionary<string, string>
{
    ["AzureCloud:CHAT:Resources:east2:Secret"] = "AZURE_ESAT2_CHAT_API_KEY",
    ["AzureCloud:CHAT:Resources:terra:Secret"] = "AZURE_FZTEST001_CHAT_API_KEY",
};
if (!assistantMode)
{
    secretNames["AzureCloud:TTS:Secret"] = "AZURE_SPEECH_KEY";
    secretNames["AzureCloud:IMAGE:Resources:east2:Secret"] = "AZURE_ESAT2_CHAT_API_KEY";
}

foreach (var (path, name) in secretNames)
{
    if (!string.IsNullOrWhiteSpace(configuration[path]))
    {
        continue;
    }

    var secret = Environment.GetEnvironmentVariable(name);
    if (OperatingSystem.IsWindows() && string.IsNullOrWhiteSpace(secret))
    {
        secret = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
    }

    if (string.IsNullOrWhiteSpace(secret))
    {
        Console.Error.WriteLine($"Set {name} or {path.Replace(":", "__", StringComparison.Ordinal)} before running the Lab.");
        return 2;
    }

    configuration[path] = secret.Trim();
}

var services = new ServiceCollection();
var ai = services.AddMicroserviceAI(configuration);
if (assistantMode)
{
    var chatConfiguration = new ConfigurationBuilder()
        .AddInMemoryCollection(configuration.GetSection("AzureCloud:CHAT").AsEnumerable())
        .Build();
    ai.AddAzure(chatConfiguration.GetSection("AzureCloud"));
}
else
{
    ai.AddAzure();
}

services.AddTransient<AzureUsageCaptureHandler>();
// Azure's configured request timeout also covers image generations that take longer than HttpClient's default.
services.AddHttpClient("MS.Microservice.AI.Azure", client => client.Timeout = Timeout.InfiniteTimeSpan)
    .AddHttpMessageHandler<AzureUsageCaptureHandler>();

using var provider = services.BuildServiceProvider();
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, signal) =>
{
    signal.Cancel = true;
    cancellation.Cancel();
};

var parent = outputArgs.SingleOrDefault() ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
var runName = $"Azure-Lab-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss'Z'}-{Guid.NewGuid().ToString("N")[..8]}";
var output = Directory.CreateDirectory(Path.Combine(Path.GetFullPath(parent), runName)).FullName;
Console.WriteLine($"Artifacts: {output}");

var lab = new AzureLabScenario(provider.GetRequiredService<IAIChatClient>(), provider.GetRequiredService<IAITtsClient>(),
    provider.GetRequiredService<IAIImageGenerationClient>(), output, cancellation.Token);
return assistantMode
    ? await lab.RunAssistantAsync(provider.GetRequiredService<IAIModelResolver>())
    : await lab.RunAsync();
