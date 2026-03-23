using Xunit;
using MS.Microservice.Infrastructure.Caching;
using MS.Microservice.Infrastructure.Caching.Store;
using System;
using System.Threading.Tasks;

namespace MS.Microservice.Infrastructure.Tests.Caching
{
    public class InMemoryCacheStoreTests
    {
        private static (InMemoryCacheStore store, IKeyStore keyStore) CreateStore()
        {
            // InMemoryKeyStore is internal, so we create it via reflection or use the public interface
            // Since InMemoryKeyStore is internal, we need to instantiate it through the same assembly
            // Actually, let's just test through InMemoryCacheStore which accepts IKeyStore
            var keyStore = CreateKeyStore();
            var store = new InMemoryCacheStore(keyStore);
            return (store, keyStore);
        }

        private static IKeyStore CreateKeyStore()
        {
            // InMemoryKeyStore is internal, use reflection
            var type = typeof(InMemoryCacheStore).Assembly.GetType("MS.Microservice.Infrastructure.Caching.Store.InMemoryKeyStore")!;
            return (IKeyStore)Activator.CreateInstance(type)!;
        }

        [Fact]
        public async Task SetAsync_ThenGetAsync_ReturnsValue()
        {
            var (store, _) = CreateStore();

            await store.SetAsync("key1", "hello");
            var result = await store.GetAsync<string>("key1");

            Assert.Equal("hello", result);
        }

        [Fact]
        public async Task GetAsync_NonExistentKey_ReturnsDefault()
        {
            var (store, _) = CreateStore();

            var result = await store.GetAsync<string>("missing");

            Assert.Null(result);
        }

        [Fact]
        public async Task ExistsAsync_ExistingKey_ReturnsTrue()
        {
            var (store, _) = CreateStore();
            await store.SetAsync("key1", 42);

            var exists = await store.ExistsAsync("key1");

            Assert.True(exists);
        }

        [Fact]
        public async Task ExistsAsync_NonExistentKey_ReturnsFalse()
        {
            var (store, _) = CreateStore();

            var exists = await store.ExistsAsync("missing");

            Assert.False(exists);
        }

        [Fact]
        public async Task RemoveAsync_RemovesKey()
        {
            var (store, _) = CreateStore();
            await store.SetAsync("key1", "value");

            await store.RemoveAsync("key1");

            var result = await store.GetAsync<string>("key1");
            Assert.Null(result);
        }

        [Fact]
        public async Task GetOrAddAsync_MissingKey_InvokesFactory()
        {
            var (store, _) = CreateStore();

            var result = await store.GetOrAddAsync("key1", () => Task.FromResult("created"));

            Assert.Equal("created", result);
        }

        [Fact]
        public async Task GetOrAddAsync_ExistingKey_ReturnsExistingValue()
        {
            var (store, _) = CreateStore();
            await store.SetAsync("key1", "original");

            var result = await store.GetOrAddAsync("key1", () => Task.FromResult("new_value"));

            Assert.Equal("original", result);
        }

        [Fact]
        public async Task SetAsync_StoresMetadata()
        {
            var (store, keyStore) = CreateStore();

            await store.SetAsync("key1", "value");

            var metadata = await keyStore.GetKeyMetadataAsync("key1");
            Assert.NotNull(metadata);
            Assert.Equal("key1", metadata!.Key);
            Assert.Equal(typeof(string), metadata.ValueType);
        }

        [Fact]
        public async Task RemoveAsync_RemovesMetadata()
        {
            var (store, keyStore) = CreateStore();
            await store.SetAsync("key1", "value");

            await store.RemoveAsync("key1");

            var metadata = await keyStore.GetKeyMetadataAsync("key1");
            Assert.Null(metadata);
        }
    }
}
