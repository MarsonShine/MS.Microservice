namespace MS.Microservice.Storage.S3;

public sealed record S3FileStorageOptions
{
    public required string BucketName { get; init; }
    /// <summary>Trusted ASCII relative segments, without leading/trailing slash; empty means the bucket root.</summary>
    public string Prefix { get; init; } = "";
    /// <summary>Existing trusted staging parent; Windows permissions are inherited from this directory.</summary>
    public required string TemporaryDirectory { get; init; }
    /// <summary>Actual remaining upload bytes; zero permits empty objects. Multipart upload is not implemented.</summary>
    public long MaximumUploadBytes { get; init; } = 64L * 1024 * 1024;
}
