using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coravel;
using Coravel.Invocable;
using Coravel.Scheduling.Schedule;
using Coravel.Scheduling.Schedule.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MS.Microservice.Tasks.Dashboard.Tests;

public sealed class DashboardTests
{
    [Fact]
    public async Task EmbeddedShellHonorsAuthenticationAndWorksUnderPathBase()
    {
        await using (var secure = await DashboardTestHost.CreateAsync())
        {
            using var page = await secure.Client.GetAsync("/task-center/");
            Assert.Equal(HttpStatusCode.Unauthorized, page.StatusCode);
        }
        await using var host = await DashboardTestHost.CreateAsync(anonymousShell: true, pathBase: "/ops");
        using var redirect = await host.Client.GetAsync("/ops/task-center");
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        Assert.Equal("/ops/task-center/", redirect.Headers.Location!.OriginalString);
        using var html = await host.Client.GetAsync("/ops/task-center/");
        html.EnsureSuccessStatusCode();
        Assert.Contains("./dashboard.js", await html.Content.ReadAsStringAsync());
        Assert.Equal("text/html", html.Content.Headers.ContentType!.MediaType);
        Assert.Contains("script-src 'self'", Assert.Single(html.Headers.GetValues("Content-Security-Policy")));
        Assert.DoesNotContain("unsafe-inline", Assert.Single(html.Headers.GetValues("Content-Security-Policy")));
        foreach (var asset in new[] { "dashboard.js", "dashboard.css" })
        {
            using var resource = await host.Client.GetAsync("/ops/task-center/" + asset);
            resource.EnsureSuccessStatusCode();
            Assert.True(resource.Content.Headers.ContentLength > 100);
        }
        using var data = await host.Client.GetAsync("/ops/task-center/api/jobs");
        Assert.Equal(HttpStatusCode.Unauthorized, data.StatusCode);
    }

    [Fact]
    public async Task ReadsAreAuthorizedScopedAndProjectOnlyPublicStatus()
    {
        await using var host = await DashboardTestHost.CreateAsync();
        var alice = await host.SubmitAsync();
        await host.SubmitAsync();
        var bob = await host.SubmitAsync("bob");
        using var unauthenticated = await host.Client.GetAsync("/task-center/api/jobs");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        host.Login("no-scope");
        using var noScope = await host.Client.GetAsync("/task-center/api/jobs");
        Assert.Equal(HttpStatusCode.Forbidden, noScope.StatusCode);
        host.Login(isOperator: false);
        using var forbiddenSchedule = await host.Client.GetAsync("/task-center/api/schedules");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenSchedule.StatusCode);
        using var list = await host.Client.GetAsync("/task-center/api/jobs?state=Queued&take=1&scope=bob");
        list.EnsureSuccessStatusCode();
        var body = await list.Content.ReadAsStringAsync();
        Assert.DoesNotContain("not-for-browser", body);
        Assert.DoesNotContain("payloadJson", body);
        Assert.DoesNotContain("scope", body);
        using var json = JsonDocument.Parse(body);
        Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
        Assert.True(json.RootElement.GetProperty("hasMore").GetBoolean());
        foreach (var route in new[] { $"jobs/{bob.JobId}", $"jobs/{bob.JobId}/attempts", "jobs/00000000-0000-0000-0000-000000000000" })
        {
            using var response = await host.Client.GetAsync("/task-center/api/" + route);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        foreach (var query in new[] { "take=0", "take=201", "state=99" })
        {
            using var response = await host.Client.GetAsync("/task-center/api/jobs?" + query);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        var own = await host.Client.GetFromJsonAsync<DashboardJob>($"/task-center/api/jobs/{alice.JobId}");
        Assert.Equal(alice.JobId, own!.JobId);
        Assert.Equal("no-store", list.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task ControlsRequirePolicyAndAntiforgeryThenPreserveStoreStateRules()
    {
        await using var host = await DashboardTestHost.CreateAsync();
        var job = await host.SubmitAsync();
        var other = await host.SubmitAsync("bob");
        host.Login(isOperator: false);
        using var denied = await host.Client.PostAsync($"/task-center/api/jobs/{job.JobId}/cancel", null);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        host.Login();
        using var noToken = await host.Client.PostAsync($"/task-center/api/jobs/{job.JobId}/cancel", null);
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        await host.ConnectAsync();
        using var crossScope = await host.Client.PostAsync($"/task-center/api/jobs/{other.JobId}/cancel", null);
        Assert.Equal(HttpStatusCode.NotFound, crossScope.StatusCode);
        using var cancelled = await host.Client.PostAsync($"/task-center/api/jobs/{job.JobId}/cancel", null);
        Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);
        Assert.Equal(JobState.Cancelled, (await host.Client.GetFromJsonAsync<DashboardJob>($"/task-center/api/jobs/{job.JobId}"))!.State);
        using var retried = await host.Client.PostAsync($"/task-center/api/jobs/{job.JobId}/retry", null);
        Assert.Equal(HttpStatusCode.NoContent, retried.StatusCode);
        using var alreadyQueued = await host.Client.PostAsync($"/task-center/api/jobs/{job.JobId}/retry", null);
        Assert.Equal(HttpStatusCode.Conflict, alreadyQueued.StatusCode);
        host.Login("bob");
        using var changedIdentity = await host.Client.PostAsync($"/task-center/api/jobs/{other.JobId}/cancel", null);
        Assert.Equal(HttpStatusCode.BadRequest, changedIdentity.StatusCode);

        await using var readOnly = await DashboardTestHost.CreateAsync(readOnly: true);
        var readOnlyJob = await readOnly.SubmitAsync();
        readOnly.Login();
        await readOnly.ConnectAsync();
        using var blocked = await readOnly.Client.PostAsync($"/task-center/api/jobs/{readOnlyJob.JobId}/cancel", null);
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
    }

    [Fact]
    public async Task ScheduleObserverForwardsNativeRulesAndDoesNotReadArgumentsOrEvaluatePredicates()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScheduler();
        services.AddTaskDashboard(options => options.ResolveScope = _ => "test");
        services.AddSingleton<InvocationCount>();
        services.AddScoped<CountingInvocable>();
        using var provider = services.BuildServiceProvider();
        var count = provider.GetRequiredService<InvocationCount>();
        var predicates = 0;
        provider.UseSchedulerWithTaskDashboard(scheduler =>
        {
            scheduler.Schedule(count.Increment).EveryMinute().WithDashboardName("内联");
            scheduler.ScheduleAsync(() => { count.Increment(); return Task.CompletedTask; }).EveryMinute();
            scheduler.OnWorker("reports");
            scheduler.Schedule<CountingInvocable>().EverySeconds(17).Monday().Zoned(TimeZoneInfo.Utc)
                .PreventOverlapping("native-lock").RunOnceAtStart().Once()
                .When(() => { predicates++; return Task.FromResult(true); }).WithDashboardName("报告");
            scheduler.ScheduleWithParams<ParameterizedInvocable>("private-constructor-value").EveryMinute();
            scheduler.ScheduleWithParams(typeof(ParameterizedInvocable), "another-private-value").Cron("* * * * *");
            scheduler.ScheduleInvocableType(typeof(CountingInvocable)).EveryMinute();
        });
        var catalog = provider.GetRequiredService<CoravelScheduleCatalog>();
        var schedules = catalog.GetSchedules();
        Assert.Equal(6, schedules.Count);
        var report = Assert.Single(schedules, plan => plan.Name == "报告");
        Assert.Equal("reports", report.Worker);
        Assert.Equal("UTC", report.TimeZoneId);
        Assert.True(report.PreventOverlapping && report.RunOnceAtStart && report.RunOnce);
        Assert.Contains("周一", report.Constraints);
        Assert.Equal("* * * * *", schedules[4].Cron);
        Assert.DoesNotContain("private", JsonSerializer.Serialize(schedules));
        Assert.Equal(0, predicates);
        var scheduler = (Scheduler)provider.GetRequiredService<IScheduler>();
        await scheduler.RunAtAsync(new DateTime(2026, 10, 19, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(6, count.Value);
        Assert.Equal(1, predicates);
        await scheduler.RunAtAsync(new DateTime(2026, 10, 19, 0, 1, 0, DateTimeKind.Utc));
        Assert.Equal(11, count.Value);
        Assert.Equal(1, predicates);
        Assert.Equal(6, catalog.GetSchedules().Count); // The catalog describes registration, including a completed Once plan.
    }
}

public sealed class InvocationCount
{
    private int _value;
    public int Value => Volatile.Read(ref _value);
    public void Increment() => Interlocked.Increment(ref _value);
}
public sealed class CountingInvocable(InvocationCount count) : IInvocable
{
    public Task Invoke() { count.Increment(); return Task.CompletedTask; }
}
public sealed class ParameterizedInvocable(InvocationCount count, string key) : IInvocable
{
    public Task Invoke() { _ = key; count.Increment(); return Task.CompletedTask; }
}
