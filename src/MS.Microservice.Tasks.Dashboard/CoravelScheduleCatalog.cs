using Coravel;
using Coravel.Invocable;
using Coravel.Scheduling.Schedule.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace MS.Microservice.Tasks.Dashboard;

/// <summary>A registration-time snapshot of a Coravel plan, without constructor arguments or predicate execution.</summary>
public sealed record DashboardSchedule(Guid Id, string Name, string? InvocableType, string Worker,
    string? Interval, string? Cron, string TimeZoneId, IReadOnlyList<string> Constraints,
    bool PreventOverlapping, bool RunOnceAtStart, bool RunOnce);

/// <summary>Records plans registered through UseSchedulerWithTaskDashboard; it does not run or edit schedules.</summary>
public sealed class CoravelScheduleCatalog
{
    private readonly object _sync = new();
    private readonly List<ScheduleEntry> _entries = [];
    public IReadOnlyList<DashboardSchedule> GetSchedules()
    {
        lock (_sync) return _entries.Select(entry => new DashboardSchedule(entry.Id, entry.Name, entry.InvocableType,
            entry.Worker, entry.Interval, entry.Cron, entry.TimeZoneId, entry.Constraints.ToArray(),
            entry.PreventOverlapping, entry.RunOnceAtStart, entry.RunOnce)).ToArray();
    }
    internal ScheduleEntry Add(Type? type, string name, string worker)
    {
        var entry = new ScheduleEntry { InvocableType = type?.FullName, Name = name, Worker = worker };
        lock (_sync) _entries.Add(entry);
        return entry;
    }
    internal void Update(ScheduleEntry entry, Action<ScheduleEntry> change) { lock (_sync) change(entry); }

    internal sealed class ScheduleEntry
    {
        internal Guid Id { get; } = Guid.NewGuid();
        internal string Name { get; set; } = "";
        internal string? InvocableType { get; set; }
        internal string Worker { get; set; } = "";
        internal string? Interval { get; set; }
        internal string? Cron { get; set; }
        internal string TimeZoneId { get; set; } = "UTC";
        internal List<string> Constraints { get; } = [];
        internal bool PreventOverlapping { get; set; }
        internal bool RunOnceAtStart { get; set; }
        internal bool RunOnce { get; set; }
    }
}

public static class CoravelDashboardExtensions
{
    /// <summary>Uses the existing Coravel scheduler and records the successful fluent configuration for display.</summary>
    public static ISchedulerConfiguration UseSchedulerWithTaskDashboard(this IServiceProvider provider, Action<IScheduler> configure)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(configure);
        var catalog = provider.GetRequiredService<CoravelScheduleCatalog>();
        return provider.UseScheduler(scheduler => configure(new ObservedScheduler(scheduler, catalog)));
    }

    /// <summary>Assigns a display name to a plan registered through the dashboard observer.</summary>
    public static IScheduledEventConfiguration WithDashboardName(this IScheduledEventConfiguration configuration, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > 200) throw new ArgumentOutOfRangeException(nameof(name));
        if (configuration is not ObservedSchedule observed)
            throw new InvalidOperationException("Register the schedule inside UseSchedulerWithTaskDashboard to name it.");
        observed.Name(name);
        return configuration;
    }

    private sealed class ObservedScheduler(IScheduler native, CoravelScheduleCatalog catalog) : IScheduler
    {
        private string _worker = "未显式指定";
        private IScheduleInterval Observe(IScheduleInterval interval, Type? type, string name)
            => new ObservedSchedule(interval, catalog, catalog.Add(type, name, _worker));
        public IScheduleInterval Schedule(Action action) => Observe(native.Schedule(action), null, "内联任务");
        public IScheduleInterval ScheduleAsync(Func<Task> action) => Observe(native.ScheduleAsync(action), null, "异步内联任务");
        public IScheduleInterval Schedule<T>() where T : IInvocable => Observe(native.Schedule<T>(), typeof(T), typeof(T).Name);
        public IScheduleInterval ScheduleWithParams<T>(params object[] parameters) where T : IInvocable
            => Observe(native.ScheduleWithParams<T>(parameters), typeof(T), typeof(T).Name);
        public IScheduleInterval ScheduleWithParams(Type type, params object[] parameters)
            => Observe(native.ScheduleWithParams(type, parameters), type, type.Name);
        public IScheduleInterval ScheduleInvocableType(Type type) => Observe(native.ScheduleInvocableType(type), type, type.Name);
        public IScheduler OnWorker(string name)
        {
            native.OnWorker(name);
            _worker = name;
            return this;
        }
    }

    private sealed class ObservedSchedule(IScheduleInterval interval, CoravelScheduleCatalog catalog,
        CoravelScheduleCatalog.ScheduleEntry entry) : IScheduleInterval, IScheduledEventConfiguration
    {
        private IScheduledEventConfiguration _configuration = null!;
        internal void Name(string name) => catalog.Update(entry, value => value.Name = name);
        private IScheduledEventConfiguration Interval(string description, Func<IScheduledEventConfiguration> apply, string? cron = null)
        {
            _configuration = apply();
            catalog.Update(entry, value => { value.Interval = description; value.Cron = cron; });
            return this;
        }
        private IScheduledEventConfiguration Configure(Func<IScheduledEventConfiguration> apply, Action<CoravelScheduleCatalog.ScheduleEntry> record)
        {
            _configuration = apply();
            catalog.Update(entry, record);
            return this;
        }
        private IScheduledEventConfiguration Day(string name, Func<IScheduledEventConfiguration> apply)
            => Configure(apply, value => { if (!value.Constraints.Contains(name)) value.Constraints.Add(name); });
        public IScheduledEventConfiguration EveryMinute() => Interval("每分钟", interval.EveryMinute);
        public IScheduledEventConfiguration EveryFiveMinutes() => Interval("每 5 分钟", interval.EveryFiveMinutes);
        public IScheduledEventConfiguration EveryTenMinutes() => Interval("每 10 分钟", interval.EveryTenMinutes);
        public IScheduledEventConfiguration EveryFifteenMinutes() => Interval("每 15 分钟", interval.EveryFifteenMinutes);
        public IScheduledEventConfiguration EveryThirtyMinutes() => Interval("每 30 分钟", interval.EveryThirtyMinutes);
        public IScheduledEventConfiguration Hourly() => Interval("每小时", interval.Hourly);
        public IScheduledEventConfiguration HourlyAt(int minute) => Interval($"每小时第 {minute} 分钟", () => interval.HourlyAt(minute));
        public IScheduledEventConfiguration Daily() => Interval("每天 00:00", interval.Daily);
        public IScheduledEventConfiguration DailyAtHour(int hour) => Interval($"每天 {hour:00}:00", () => interval.DailyAtHour(hour));
        public IScheduledEventConfiguration DailyAt(int hour, int minute) => Interval($"每天 {hour:00}:{minute:00}", () => interval.DailyAt(hour, minute));
        public IScheduledEventConfiguration Weekly() => Interval("每周一 00:00", interval.Weekly);
        public IScheduledEventConfiguration Monthly() => Interval("每月 1 日 00:00", interval.Monthly);
        public IScheduledEventConfiguration Cron(string expression) => Interval("Cron", () => interval.Cron(expression), expression);
        public IScheduledEventConfiguration EverySecond() => Interval("每秒", interval.EverySecond);
        public IScheduledEventConfiguration EveryFiveSeconds() => Interval("每 5 秒", interval.EveryFiveSeconds);
        public IScheduledEventConfiguration EveryTenSeconds() => Interval("每 10 秒", interval.EveryTenSeconds);
        public IScheduledEventConfiguration EveryFifteenSeconds() => Interval("每 15 秒", interval.EveryFifteenSeconds);
        public IScheduledEventConfiguration EveryThirtySeconds() => Interval("每 30 秒", interval.EveryThirtySeconds);
        public IScheduledEventConfiguration EverySeconds(int seconds) => Interval($"每 {seconds} 秒（分钟内刻度）", () => interval.EverySeconds(seconds));
        public IScheduledEventConfiguration Monday() => Day("周一", () => _configuration.Monday());
        public IScheduledEventConfiguration Tuesday() => Day("周二", () => _configuration.Tuesday());
        public IScheduledEventConfiguration Wednesday() => Day("周三", () => _configuration.Wednesday());
        public IScheduledEventConfiguration Thursday() => Day("周四", () => _configuration.Thursday());
        public IScheduledEventConfiguration Friday() => Day("周五", () => _configuration.Friday());
        public IScheduledEventConfiguration Saturday() => Day("周六", () => _configuration.Saturday());
        public IScheduledEventConfiguration Sunday() => Day("周日", () => _configuration.Sunday());
        public IScheduledEventConfiguration Weekday() => Day("工作日", () => _configuration.Weekday());
        public IScheduledEventConfiguration Weekend() => Day("周末", () => _configuration.Weekend());
        public IScheduledEventConfiguration PreventOverlapping(string identifier) => Configure(() => _configuration.PreventOverlapping(identifier), value => value.PreventOverlapping = true);
        public IScheduledEventConfiguration When(Func<Task<bool>> predicate) => Day("自定义条件", () => _configuration.When(predicate));
        public IScheduledEventConfiguration Zoned(TimeZoneInfo zone) => Configure(() => _configuration.Zoned(zone), value => value.TimeZoneId = zone.Id);
        public IScheduledEventConfiguration RunOnceAtStart() => Configure(() => _configuration.RunOnceAtStart(), value => value.RunOnceAtStart = true);
        public IScheduledEventConfiguration Once() => Configure(() => _configuration.Once(), value => value.RunOnce = true);
    }
}
