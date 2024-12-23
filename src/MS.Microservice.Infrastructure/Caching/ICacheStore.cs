using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MS.Microservice.Infrastructure.Caching
{
	public interface ICacheStore
	{
		Task<T?> GetAsync<T>(string key);
		Task<T?> GetOrAddAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null);
		Task SetAsync<T>(string key, T value, TimeSpan? expiration = null);
		Task RemoveAsync(string key);
		Task<bool> ExistsAsync(string key);
	}
}
