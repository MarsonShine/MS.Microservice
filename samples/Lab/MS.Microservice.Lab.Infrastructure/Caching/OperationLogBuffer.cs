using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MS.Microservice.Infrastructure.Caching
{
	internal class OperationLogBuffer : IDisposable
	{
		private readonly ConcurrentQueue<CacheOperation> _operationBuffer = new();
		private readonly IKeyStore _keyStore;
		private readonly CacheOperationLogOptions _options;
		private readonly PeriodicTimer _flushTimer;
		private readonly Lock _flushLock = new();
		private bool _isDisposed;

		public OperationLogBuffer(IKeyStore keyStore, CacheOperationLogOptions options)
		{
			_keyStore = keyStore;
			_options = options;
			_flushTimer = new PeriodicTimer(TimeSpan.FromSeconds(30));
		}

		public void AddOperation(CacheOperation operation)
		{
			if (!_options.EnableOperationLog) return;

			_operationBuffer.Enqueue(operation);

			// 如果达到缓冲区大小，触发刷新
			if (_operationBuffer.Count >= _options.BufferSize)
			{
				Task.Run(FlushAsync);
			}
		}

		private void FlushCallback(object state)
		{
			Task.Run(FlushAsync);
		}

		public async Task FlushAsync()
		{
			await Task.CompletedTask;
			if (_isDisposed) return;

			lock (_flushLock)
			{
				if (_operationBuffer.IsEmpty) return;

				// 按key分组批量处理
				var operationGroups = _operationBuffer
					.ToList()
					//.GroupBy(op => op.Key)
					;

				//foreach (var group in operationGroups)
				//{
				//	var key = group.Key;
				//	var operations = group.ToList();

				//	Task.Run(async () =>
				//	{
				//		try
				//		{
				//			await ProcessOperationGroupAsync(key, operations);
				//		}
				//		catch (Exception ex)
				//		{
				//			// 处理错误，可以添加重试逻辑
				//			Console.WriteLine($"Error processing operations for key {key}: {ex.Message}");
				//		}
				//	});
				//}

				// 清空缓冲区
				_operationBuffer.Clear();
			}
		}

		private async Task ProcessOperationGroupAsync(string key, List<CacheOperation> operations)
		{
			var metadata = await _keyStore.GetKeyMetadataAsync(key);
			if (metadata == null) return;

			// 更新统计信息
			foreach (var operation in operations)
			{
				switch (operation.OperationType)
				{
					case CacheOperationType.Get:
						metadata.GetCount++;
						metadata.LastAccessTime = operation.OperationTime;
						break;
					case CacheOperationType.Set:
						metadata.SetCount++;
						metadata.LastUpdateTime = operation.OperationTime;
						break;
					case CacheOperationType.Update:
						metadata.UpdateCount++;
						metadata.LastUpdateTime = operation.OperationTime;
						break;
				}

				// 只有在需要详细日志时才添加操作记录
				if (_options.DetailedLog)
				{
					metadata.Operations.Add(operation);
				}
			}

			await _keyStore.UpdateKeyMetadataAsync(metadata);
		}

		public void Dispose()
		{
			if (_isDisposed) return;

			_isDisposed = true;
			_flushTimer?.Dispose();
			FlushAsync().Wait();
		}
	}
}
