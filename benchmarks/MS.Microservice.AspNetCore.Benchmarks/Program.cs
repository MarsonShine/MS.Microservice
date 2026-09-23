using System.Diagnostics;
using System.Security.Claims;
using MS.Microservice.AspNetCore;

const int iterations = 200_000;
const int rounds = 5;

var single = Principal("profiles.manage");
var multiple = Principal("read write", "reports.view profiles.manage");
var missing = Principal("read write", "reports.view audit.view");

Measure("single match", () => ExternalIdentityExtensions.HasPermission(single, "scope", "profiles.manage"));
Measure("multiple claims, last match", () => ExternalIdentityExtensions.HasPermission(multiple, "scope", "profiles.manage"));
Measure("multiple claims, miss", () => ExternalIdentityExtensions.HasPermission(missing, "scope", "profiles.manage"));

static ClaimsPrincipal Principal(params string[] scopes)
    => new(new ClaimsIdentity(scopes.Select(scope => new Claim("scope", scope))));

void Measure(string name, Func<bool> check)
{
    for (var i = 0; i < iterations / 10; i++) _ = check();

    var times = new double[rounds];
    var allocations = new double[rounds];
    for (var round = 0; round < rounds; round++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        var matches = 0;
        for (var i = 0; i < iterations; i++)
            if (check()) matches++;
        times[round] = Stopwatch.GetElapsedTime(start).TotalNanoseconds / iterations;
        allocations[round] = (double)(GC.GetAllocatedBytesForCurrentThread() - before) / iterations;
        GC.KeepAlive(matches);
    }

    Array.Sort(times);
    Array.Sort(allocations);
    Console.WriteLine($"{name}: {times[rounds / 2]:F1} ns/op, {allocations[rounds / 2]:F1} B/op (median of {rounds} x {iterations:N0})");
}
