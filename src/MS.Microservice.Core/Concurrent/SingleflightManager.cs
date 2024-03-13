using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

/*
 * singleflifght 模式是一种并发控制模式，解决在并发场景下的重复操作和请求穿透问题。
 * 如果有多个并发请求要执行相同的操作,那么只有一个请求会真正执行该操作,其他请求会阻塞并等待该操作完成后,共享同一个结果。
 * 这样可以避免重复计算,提高系统性能和资源利用率。
 * 具体使用场景和使用优劣可详见：https://levelup.gitconnected.com/singleflight-concurrency-design-pattern-in-golang-f4ce5c1ce87e
 */
namespace MS.Microservice.Core.Concurrent
{
	public class SingleflightManager
	{
		private readonly ConcurrentDictionary<string, TaskCompletionSource<object>> _inFlightRequests;

		public SingleflightManager()
		{
			_inFlightRequests = new ConcurrentDictionary<string, TaskCompletionSource<object>>();
		}

		public async Task<T> ExecuteOnceAsync<T>(string key, Func<Task<T>> action)
		{
			var completionSource = new TaskCompletionSource<object>();
			var existingTask = _inFlightRequests.GetOrAdd(key, completionSource);
			try
			{
				if (existingTask == completionSource)
				{
					// 当前请求是第一个请求，执行对应的操作
					T result = await action();
					completionSource.SetResult(result!);
					return result;
				}
				else
				{
					// 等待第一个请求完成，并使用其结果
					return (T)(await (existingTask.Task));
				}
			}
			finally
			{
				_inFlightRequests.TryRemove(key, out _);
			}
		}
	}
}
