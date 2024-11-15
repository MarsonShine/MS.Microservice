using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MS.Microservice.Infrastructure.Caching.Buffer
{
	public class BufferQueue<T> : IBufferQueue<T>
	{
		private readonly ConcurrentQueue<T> _primaryBuffer = new(); // 主缓冲区>
		//private readonly ConcurrentQueue<T> _secondaryBuffer = new();
		private readonly BufferQueueOptions _options;
		private readonly Func<IEnumerable<T>, Task> _proccessAsync;
		private readonly PeriodicTimer _timer;
		private readonly SemaphoreSlim _flushLock = new(1, 1);
		private readonly CancellationTokenSource _cts = new();
		private volatile bool _isProcessing;

		public BufferQueue(BufferQueueOptions? options = null, Func<IEnumerable<T>, Task>? proccessAsync = null)
		{
			_options = options ?? new BufferQueueOptions();
			_proccessAsync = proccessAsync ?? throw new ArgumentNullException(nameof(proccessAsync));
			_timer = new(options!.MaxBufferTime);

			if(_options.AutoStart) 
				StartAsync().ConfigureAwait(false);
		}


		public void Add(T item)
		{
			_primaryBuffer.Enqueue(item);

			// 如果达到主缓冲区大小阈值，触发异步刷新
			if (_primaryBuffer.Count >= _options.BufferSize)
			{
				_ = FlushAsync();
			}
		}

		public async Task FlushAsync()
		{
			if (_isProcessing) return; 
			if (_primaryBuffer.IsEmpty) return;
			try
			{
				await _flushLock.WaitAsync(_cts.Token);
				_isProcessing = true;

				// 处理数据
				var items = new List<T>();
				while (_primaryBuffer.TryDequeue(out var item))
				{
					items.Add(item);
				}

				if (items.Count != 0)
				{
					try
					{
						await _proccessAsync(items);
					}
					catch (Exception)
					{
						// 处理失败时，将数据放回主缓冲区
						foreach (var item in items)
						{
							_primaryBuffer.Enqueue(item);
						}
						throw;
					}
				}
			}
			finally
			{
				_isProcessing = false;
				if (_flushLock.CurrentCount == 0)
				{
					_flushLock.Release();
				}
			}
		}

		public async Task StartAsync()
		{
			while (await _timer.WaitForNextTickAsync(_cts.Token))
			{
				if (!_primaryBuffer.IsEmpty)
					await FlushAsync();
			}
		}

		public async Task StopAsync()
		{
			if (_cts.IsCancellationRequested)
				return;

			await _cts.CancelAsync();
		}
	}
}
