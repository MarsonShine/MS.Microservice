using System.Security.Cryptography;

namespace MS.Microservice.Media.Inference;

internal static class VerifiedOnnxModel
{
    internal static async Task<byte[]> ReadAsync(string path, int expectedBytes, string expectedSha256, CancellationToken cancellationToken)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (file.Length != expectedBytes) throw new InvalidDataException("The local model has an unexpected size.");
        var bytes = new byte[expectedBytes];
        await file.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        if (await file.ReadAsync(new byte[1], cancellationToken).ConfigureAwait(false) != 0
            || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), Convert.FromHexString(expectedSha256)))
            throw new InvalidDataException("The local model does not match the supported SHA-256.");
        cancellationToken.ThrowIfCancellationRequested();
        return bytes;
    }
}
