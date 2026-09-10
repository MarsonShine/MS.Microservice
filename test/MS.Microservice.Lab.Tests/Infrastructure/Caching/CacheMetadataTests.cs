using Xunit;
using MS.Microservice.Infrastructure.Caching;

namespace MS.Microservice.Infrastructure.Tests.Caching
{
    public class CacheMetadataTests
    {
        [Fact]
        public void AddOperation_Get_IncrementsGetCount()
        {
            var metadata = new CacheMetadata { Key = "test" };
            var op = CacheOperation.Success(CacheOperationType.Get, DateTimeOffset.Now);

            metadata.AddOperation(op);

            Assert.Equal(1, metadata.GetCount);
            Assert.Equal(op.OperationTime, metadata.LastAccessTime);
            Assert.Single(metadata.Operations);
        }

        [Fact]
        public void AddOperation_Set_IncrementsSetCount()
        {
            var metadata = new CacheMetadata { Key = "test" };
            var op = CacheOperation.Success(CacheOperationType.Set, DateTimeOffset.Now);

            metadata.AddOperation(op);

            Assert.Equal(1, metadata.SetCount);
            Assert.Equal(op.OperationTime, metadata.LastUpdateTime);
        }

        [Fact]
        public void AddOperation_Update_IncrementsUpdateCount()
        {
            var metadata = new CacheMetadata { Key = "test" };
            var op = CacheOperation.Success(CacheOperationType.Update, DateTimeOffset.Now);

            metadata.AddOperation(op);

            Assert.Equal(1, metadata.UpdateCount);
            Assert.Equal(op.OperationTime, metadata.LastUpdateTime);
        }

        [Fact]
        public void AddOperation_MultipleGets_IncrementsCorrectly()
        {
            var metadata = new CacheMetadata { Key = "test" };
            metadata.AddOperation(CacheOperation.Success(CacheOperationType.Get, DateTimeOffset.Now));
            metadata.AddOperation(CacheOperation.Success(CacheOperationType.Get, DateTimeOffset.Now));
            metadata.AddOperation(CacheOperation.Success(CacheOperationType.Get, DateTimeOffset.Now));

            Assert.Equal(3, metadata.GetCount);
            Assert.Equal(3, metadata.Operations.Count);
        }
    }

    public class CacheOperationTests
    {
        [Fact]
        public void Success_CreatesSuccessOperation()
        {
            var time = DateTimeOffset.Now;
            var op = CacheOperation.Success(CacheOperationType.Get, time);

            Assert.True(op.IsSuccess);
            Assert.Equal(CacheOperationType.Get, op.OperationType);
            Assert.Equal(time, op.OperationTime);
        }

        [Fact]
        public void Failed_CreatesFailedOperation()
        {
            var time = DateTimeOffset.Now;
            var op = CacheOperation.Failed(CacheOperationType.Set, time, "timeout");

            Assert.False(op.IsSuccess);
            Assert.Equal(CacheOperationType.Set, op.OperationType);
            Assert.Equal("timeout", op.ErrorMessage);
        }
    }
}
