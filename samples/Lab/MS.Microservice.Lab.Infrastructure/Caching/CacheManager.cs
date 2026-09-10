using System.Collections.Concurrent;

namespace MS.Microservice.Infrastructure.Caching
{
    public class CacheManager(IKeyStore keyStore, ICacheStore cacheStore, CacheOperationLogOptions options) : IDisposable
    {
        private readonly IKeyStore _keyStore = keyStore ?? throw new ArgumentNullException(nameof(keyStore));
        private readonly ICacheStore _cache = cacheStore ?? throw new ArgumentNullException(nameof(cacheStore));
        private readonly CacheOperationLogOptions _options = options ?? throw new ArgumentNullException(nameof(options));
        private readonly OperationLogBuffer _operationBuffer = new(keyStore, options);
        private readonly ConcurrentDictionary<string, Lazy<Task<object>>> _inFlightFactories = new();

        public async Task<bool> ExistsAsync(string key)
        {
            ValidateKey(key);

            var metadata = await _keyStore.GetKeyMetadataAsync(key);
            if (metadata is not null && IsExpired(metadata))
            {
                await RemoveAsync(key);
                return false;
            }

            return await _cache.ExistsAsync(key);
        }

        public async Task<CacheItem<T>?> GetAsync<T>(string key)
        {
            ValidateKey(key);
            var operation = CreateOperation(CacheOperationType.Get);

            try
            {
                var item = await _cache.GetAsync<CacheItem<T>>(key);
                if (item is null || IsExpired(item.Metadata))
                {
                    if (item is not null)
                    {
                        await RemoveAsync(key);
                    }

                    operation.IsSuccess = false;
                    _operationBuffer.AddOperation(operation);
                    return null;
                }

                operation.IsSuccess = true;
                _operationBuffer.AddOperation(operation);
                return item;
            }
            catch (Exception ex)
            {
                RecordFailure(operation, ex);
                throw;
            }
        }

        public async Task<CacheItem<T>> GetOrAddAsync<T>(
            string key,
            Func<Task<T>> factory,
            TimeSpan? expiration = null)
        {
            ValidateKey(key);
            ArgumentNullException.ThrowIfNull(factory);

            var cached = await GetAsync<T>(key);
            if (cached is not null)
            {
                return cached;
            }

            var lazyFactory = new Lazy<Task<object>>(
                async () => await CreateAndCacheAsync(key, factory, expiration),
                LazyThreadSafetyMode.ExecutionAndPublication);
            var activeFactory = _inFlightFactories.GetOrAdd(key, lazyFactory);

            try
            {
                var item = await activeFactory.Value;
                return item as CacheItem<T>
                    ?? throw new InvalidOperationException(
                        $"Cache key '{key}' is already being populated with a different value type.");
            }
            finally
            {
                ((ICollection<KeyValuePair<string, Lazy<Task<object>>>>)_inFlightFactories)
                    .Remove(new KeyValuePair<string, Lazy<Task<object>>>(key, activeFactory));
            }
        }

        public async Task RemoveAsync(string key)
        {
            ValidateKey(key);
            var operation = CreateOperation(CacheOperationType.Remove);

            try
            {
                await _cache.RemoveAsync(key);
                await _keyStore.RemoveKeyMetadataAsync(key);
                operation.IsSuccess = true;
                _operationBuffer.AddOperation(operation);
            }
            catch (Exception ex)
            {
                RecordFailure(operation, ex);
                throw;
            }
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null)
        {
            ValidateKey(key);
            await CreateCacheItemAsync(key, value, expiration);
        }

        public void Dispose()
        {
            _operationBuffer.Dispose();
            GC.SuppressFinalize(this);
        }

        private async Task<object> CreateAndCacheAsync<T>(
            string key,
            Func<Task<T>> factory,
            TimeSpan? expiration)
        {
            var cached = await GetAsync<T>(key);
            if (cached is not null)
            {
                return cached;
            }

            var value = await factory();
            return await CreateCacheItemAsync(key, value, expiration);
        }

        private async Task<CacheItem<T>> CreateCacheItemAsync<T>(
            string key,
            T value,
            TimeSpan? expiration)
        {
            var now = DateTimeOffset.UtcNow;
            var operation = CreateOperation(CacheOperationType.Set, now);
            var metadata = new CacheMetadata
            {
                Key = key,
                ValueType = typeof(T),
                CreatedTime = now,
                LastUpdateTime = now,
                ExpirationTime = expiration,
                SetCount = 1,
                Operations = _options.DetailedLog ? [operation] : []
            };
            var item = new CacheItem<T>(value, metadata);

            try
            {
                await _cache.SetAsync(key, item, expiration);
                await _keyStore.UpdateKeyMetadataAsync(metadata);
                operation.IsSuccess = true;
                _operationBuffer.AddOperation(operation);
                return item;
            }
            catch (Exception ex)
            {
                RecordFailure(operation, ex);
                throw;
            }
        }

        private static bool IsExpired(CacheMetadata metadata)
            => metadata.ExpirationTime is { } expiration
                && metadata.CreatedTime + expiration <= DateTimeOffset.UtcNow;

        private static CacheOperation CreateOperation(
            CacheOperationType operationType,
            DateTimeOffset? operationTime = null)
            => new()
            {
                OperationType = operationType,
                OperationTime = operationTime ?? DateTimeOffset.UtcNow
            };

        private void RecordFailure(CacheOperation operation, Exception exception)
        {
            operation.IsSuccess = false;
            operation.ErrorMessage = exception.Message;
            _operationBuffer.AddOperation(operation);
        }

        private static void ValidateKey(string key)
            => ArgumentException.ThrowIfNullOrWhiteSpace(key);
    }
}
