using System;
using System.Diagnostics.CodeAnalysis;

namespace MS.Microservice.Infrastructure.Caching
{
	public class CacheOperation
	{
		public CacheOperationType OperationType { get; set; } = default!;
		public DateTimeOffset OperationTime { get; set; }
		public bool IsSuccess { get; set; }
		[MemberNotNullWhen(false, nameof(IsSuccess))]
		public string ErrorMessage { get; set; } = default!;

		public static CacheOperation Success(CacheOperationType operationType, DateTimeOffset operationTime) => new() {OperationType = operationType, OperationTime = operationTime, IsSuccess = true };

		public static CacheOperation Failed(CacheOperationType operationType, DateTimeOffset operationTime, string errorMessage) => new() { OperationType = operationType, OperationTime = operationTime, IsSuccess = false, ErrorMessage = errorMessage };
	}
}
