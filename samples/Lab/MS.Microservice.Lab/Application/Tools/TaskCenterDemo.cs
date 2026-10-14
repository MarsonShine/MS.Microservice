using Coravel;
using Microsoft.EntityFrameworkCore;
using MS.Microservice.Lab.Controller;
using MS.Microservice.Search;
using MS.Microservice.Tasks;
using MS.Microservice.Tasks.Coravel;
using MS.Microservice.Tasks.EFCore;

namespace MS.Microservice.Lab.Application.Tools;

internal sealed class LabTasksDbContext(DbContextOptions<LabTasksDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddTaskCenter();
}

internal static class TaskCenterDemo
{
    internal const string IndexQueue = "document-index";
    internal const string IndexHandler = "documents.index";
    internal static string Scope(string owner) => "lab:" + DemoFileStore.OwnerTag(owner);

    internal static IServiceCollection AddTaskCenterDemo(this IServiceCollection services,
        IWebHostEnvironment environment, IConfiguration configuration)
    {
        var taskDirectory = PrivateDirectory(environment, configuration, "TaskCenterDirectory", ".tools/task-center");
        var indexDirectory = PrivateDirectory(environment, configuration, "SearchIndexDirectory", ".tools/document-search");
        Directory.CreateDirectory(taskDirectory);
        services.AddDbContext<LabTasksDbContext>(options => options.UseSqlite(
            new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(taskDirectory, "tasks.db"), Pooling = false
            }.ToString()));
        services.AddEfCoreJobStore<LabTasksDbContext>();
        services.AddSingleton(_ => new DemoFileStore(environment, configuration));
        services.AddSingleton(_ => new DocumentSearchIndex(new() { DirectoryPath = indexDirectory }));
        // This sample owns a separate SQLite database. Production hosts apply their own EF migrations.
        services.AddHostedService<LabTasksDatabaseInitializer>();
        services.AddScheduler();
        services.AddTaskExecution(options => options.AddQueue(IndexQueue, 1))
            .AddJobHandler<DocumentIndexPayload, DocumentIndexJobHandler>(IndexHandler, 1, options =>
            {
                options.Queue = IndexQueue;
                options.SafeToRetry = true;
                options.Timeout = TimeSpan.FromMinutes(10);
            });
        services.AddCoravelTaskCenter();
        return services;
    }

    private static string PrivateDirectory(IWebHostEnvironment environment, IConfiguration configuration,
        string name, string defaultPath)
    {
        var directory = Path.GetFullPath(Path.Combine(environment.ContentRootPath,
            ToolDemoDefaults.Value(configuration, name) ?? defaultPath));
        var webRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(environment.WebRootPath
            ?? Path.Combine(environment.ContentRootPath, "wwwroot")));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (directory.Equals(webRoot, comparison) || directory.StartsWith(webRoot + Path.DirectorySeparatorChar, comparison))
            throw new ToolDependencyException("ToolDemos:" + name + "（必须位于 webroot 之外）");
        return directory;
    }
}

internal sealed class LabTasksDatabaseInitializer(IServiceScopeFactory scopes) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<LabTasksDbContext>().Database.EnsureCreatedAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
