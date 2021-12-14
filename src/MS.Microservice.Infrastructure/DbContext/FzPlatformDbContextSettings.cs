namespace MS.Microservice.Infrastructure.DbContext
{
    public class MsPlatformDbContextSettings
    {
        /// <summary>
        /// 自动开启时间追踪，实体更新时，自动更新时间字段，详见<see cref="ICreatedAt"/>以及<seealso cref="IUpdatedAt"/>
        /// </summary>
        public string AutoTimeTracker { get; set; } = "Disabled";
        /// <summary>
        /// 是否开启软删除
        /// </summary>
        public bool EnabledSoftDeleted { get; set; } = true;

        internal bool EnabledAutoTimeTracker() => AutoTimeTracker == "Enabled";
    }
}
