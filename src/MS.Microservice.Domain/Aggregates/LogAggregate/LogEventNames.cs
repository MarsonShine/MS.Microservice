namespace MS.Microservice.Domain.Aggregates.LogAggregate
{
    public class LogEventNames
    {
        public static readonly string BatchCodeImportCreated = "激活码导入创建";
        public static readonly string BatchCodeAppended = "激活码追加";
        public static readonly string BatchUpdated = "批次更新";
        public static readonly string BatchStatusEnabled = "批次状态启动";

        public static readonly string ActivateCodeDiscarded = "激活码废弃";
        public static readonly string ActivateCodeActivated = "激活码核销";

        public static readonly string OrderRefunded = "订单退款";
    }
}
