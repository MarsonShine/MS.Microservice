using System;

namespace MS.Microservice.Infrastructure.Caching
{
	public class CacheOperationLogOptions
	{
		/// <summary>
		/// 是否开启日志操作记录
		/// </summary>
		public bool EnableOperationLog { get; set; } = true;
		/// <summary>
		/// 缓冲区大小
		/// </summary>
		public int BufferSize { get; set; } = 1000;
		/// <summary>
		/// 定期刷新间隔
		/// </summary>
		public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(30);
		/// <summary>
		/// 是否记录详细日志
		/// </summary>
		public bool DetailedLog { get; set; } = false;
	}
}
