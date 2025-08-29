namespace MS.Microservice.Web.Infrastructure.LogUtils.Nlog.Configs
{
    public class LoggerConfig
    {
        public string PathFileName { get; set; } = "logs/log-{Hour}.log";
        public string MessageTemplate { get; set; } = "[{RequestId}] [{Timestamp:HH:mm:ss} [{AppRequestId} {PlatformId} {UserFlag}] {Level:u3}] {Message:lj} {NewLine}{Exception}";
        public int FileSizeLimit { get; set; } = 1073741824; // 1Gb
        /// <summary>
        /// 默认本地地址：tcp://127.0.0.1:5000
        /// </summary>
        public string NetAddress { get; set; } = "tcp://127.0.0.1:5000";
        /// <summary>
        /// 数据库连接字符串
        /// </summary>
        public string? DbConnectionString { get; set; }
        public string LogLevel { get; set; } = Microsoft.Extensions.Logging.LogLevel.Information.ToString();
    }
}
