using MS.Microservice.Core.IO;
using Xunit;

namespace MS.Microservice.Core.Tests.IO;

public sealed class LocalFileStorageCleanupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MS.Microservice.LocalFileStorageCleanupTests", Guid.NewGuid().ToString("N"));

    [WindowsStorageTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task FailedWriteAndFailedCleanup_PreserveBothErrorsAndExistingFiles(int variant)
    {
        var key = StoredFileKey.Create(".bin");
        string? temporary = null;
        using var cancellation = new CancellationTokenSource();
        using var input = new CallbackStream(() =>
        {
            temporary = Assert.Single(Directory.GetFiles(_root, ".*.tmp"));
            File.SetAttributes(temporary, FileAttributes.ReadOnly);
            switch (variant)
            {
                case 0: throw new IOException("source I/O failure");
                case 1: throw new InvalidDataException("source format failure");
                case 2: cancellation.Cancel(); break;
                case 3: File.WriteAllText(Path.Combine(_root, key), "keep"); break;
            }
        });
        try
        {
            var error = await Assert.ThrowsAsync<AggregateException>(() => new LocalFileStorage(_root).WriteNewAsync(key, input, cancellation.Token));
            Assert.Equal(2, error.InnerExceptions.Count);
            switch (variant)
            {
                case 0: Assert.Equal("source I/O failure", Assert.IsType<IOException>(error.InnerExceptions[0]).Message); break;
                case 1: Assert.Equal("source format failure", Assert.IsType<InvalidDataException>(error.InnerExceptions[0]).Message); break;
                case 2: Assert.IsAssignableFrom<OperationCanceledException>(error.InnerExceptions[0]); break;
                case 3: Assert.IsType<IOException>(error.InnerExceptions[0]); break;
            }
            Assert.IsType<UnauthorizedAccessException>(error.InnerExceptions[1]);
            Assert.True(input.CanRead);
            Assert.True(File.Exists(temporary));
            if (variant == 3) Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(_root, key)));
            else Assert.False(File.Exists(Path.Combine(_root, key)));
        }
        finally { if (temporary is not null && File.Exists(temporary)) File.SetAttributes(temporary, FileAttributes.Normal); }
    }

    private sealed class CallbackStream(Action callback) : MemoryStream
    {
        public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            await destination.WriteAsync(new byte[] { 1, 2 }, cancellationToken);
            callback();
        }
    }

    public void Dispose()
    {
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "MS.Microservice.LocalFileStorageCleanupTests")) + Path.DirectorySeparatorChar;
        if (Path.GetFullPath(_root).StartsWith(parent, StringComparison.Ordinal) && Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}

internal sealed class WindowsStorageTheoryAttribute : TheoryAttribute
{
    public WindowsStorageTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Uses Windows readonly-file deletion semantics to force a real cleanup failure.";
    }
}
