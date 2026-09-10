using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Reference.Application;

namespace MS.Microservice.Messaging.FaultWorker;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Contains("--barrier-only"))
            {
                Console.WriteLine("READY");
                await Console.In.ReadLineAsync();
                await new FaultBarrier("protocol").ReachAsync("protocol", Guid.NewGuid(), default);
                return 0;
            }
            var environment = new FaultEnvironment(Required("MS_FAULT_PROVIDER"), Required("MS_FAULT_DATABASE"),
                Required("MS_FAULT_BROKER"), Required("MS_FAULT_PREFIX"));
            using var host = FaultHost.Build(environment, Environment.GetEnvironmentVariable("MS_FAULT_PHASE"));
            await host.StartAsync();
            Console.WriteLine("READY");
            while (await Console.In.ReadLineAsync() is { } command)
            {
                if (command == "stop") break;
                if (command != "produce") throw new ArgumentException("Unknown worker command.");
                using var scope = host.Services.CreateScope();
                var result = await scope.ServiceProvider.GetRequiredService<ProfileService>().CreateAsync(
                    new("https://identity.example", Guid.NewGuid().ToString(), "故障恢复档案", ["reader"]),
                    new("https://identity.example", "operator"));
                if (!result.IsRight) throw new InvalidOperationException("Business operation failed.");
                Console.WriteLine($"COMMITTED|{result.Right.Id:D}");
            }
            await host.StopAsync();
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"WORKER_FAILED|{exception.GetType().Name}");
            return 1;
        }
    }

    private static string Required(string key) => Environment.GetEnvironmentVariable(key)
        ?? throw new ArgumentException($"Missing {key}");
}
