using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MS.Microservice.Infrastructure.Caching
{
	public interface IKeyStore
	{
		Task StoreKeyMetadataAsync(CacheMetadata metadata);
		Task<IEnumerable<CacheMetadata>> GetAllKeyMetadataAsync();
		Task<CacheMetadata?> GetKeyMetadataAsync(string key);
		Task RemoveKeyMetadataAsync(string key);
		Task UpdateKeyMetadataAsync(CacheMetadata metadata);
		Task LogOperationAsync(string key, CacheOperation operation);
	}
}
