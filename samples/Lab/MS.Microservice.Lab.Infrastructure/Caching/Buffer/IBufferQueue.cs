using System.Collections.Generic;
using System.Threading.Tasks;

namespace MS.Microservice.Infrastructure.Caching.Buffer
{
	public interface IBufferQueue<T>
	{
		void Add(T item);
		Task FlushAsync();
		Task StartAsync();
		Task StopAsync();
	}
}
