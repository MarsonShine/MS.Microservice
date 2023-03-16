namespace MS.Microservice.Domain.Aggregates.LogAggregate
{
    /// <summary>
    /// 日志事件类型
    /// </summary>
    public enum LogEventTypeEnum
    {
        /// <summary>
        /// 核销
        /// </summary>
        Activation,
        /// <summary>
        /// 创建
        /// </summary>
        Create,
        /// <summary>
        /// 追加
        /// </summary>
        Append,
        /// <summary>
        /// 更新
        /// </summary>
        Update,
        /// <summary>
        /// 删除
        /// </summary>
        Delete,
        /// <summary>
        /// 回收
        /// </summary>
        Reycle,
    }
}
