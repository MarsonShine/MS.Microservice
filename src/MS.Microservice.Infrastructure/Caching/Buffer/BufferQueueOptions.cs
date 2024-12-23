using System;

namespace MS.Microservice.Infrastructure.Caching.Buffer
{
	public class BufferQueueOptions
	{
		/// <summary>
		/// 缓冲队列大小
		/// </summary>
		public int BufferSize { get; set; } = 1000;
		/// <summary>
		/// 最大兜底时间，防止数据在缓冲区停留过长
		/// </summary>
		public TimeSpan MaxBufferTime { get; set; } = TimeSpan.FromSeconds(15);
		public bool AutoStart { get; set; }
	}
}
