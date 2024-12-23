namespace MS.Microservice.Infrastructure.Caching
{
	public record CacheItem<T>(T Value, CacheMetadata Metadata);
}
