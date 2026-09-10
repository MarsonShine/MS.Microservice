using System;
using System.Collections.Generic;

namespace MS.Microservice.Infrastructure.Caching
{
	public class CacheMetadata
	{
		public string Key { get; set; } = null!;
		public Type ValueType { get; set; } = default!;
		public DateTimeOffset CreatedTime { get; set; }
		public DateTimeOffset? LastAccessTime { get; set; }
		public DateTimeOffset? LastUpdateTime { get; set; }
		public long GetCount { get; set; }
		public long SetCount { get; set; }
		public long UpdateCount { get; set; }
		public TimeSpan? ExpirationTime { get; set; }
		public List<CacheOperation> Operations { get; set; } = [];

		public void AddOperation(CacheOperation operation)
		{
			Operations.Add(operation);
			switch (operation.OperationType)
			{
				case CacheOperationType.Get:
					GetCount++;
					LastAccessTime = operation.OperationTime;
					break;
				case CacheOperationType.Set:
					SetCount++;
					LastUpdateTime = operation.OperationTime;
					break;
				case CacheOperationType.Update:
					UpdateCount++;
					LastUpdateTime = operation.OperationTime;
					break;
			}
		}
	}
}
