using System.Net;
using Microsoft.Extensions.Logging;

namespace MS.Microservice.TestSupport;

internal static class HttpLoggingTestSupport
{
    internal sealed class ProbeContent(byte[] bytes, bool gated = false) : HttpContent
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Serializations { get; private set; }
        public bool Disposed { get; private set; }
        public ReadOnlyMemory<byte> Prefix => bytes.AsMemory(0, bytes.Length / 2);

        protected override bool TryComputeLength(out long length) { length = bytes.Length; return true; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken token)
        {
            Serializations++;
            await stream.WriteAsync(Prefix, token);
            Started.TrySetResult();
            if (gated) await Release.Task.WaitAsync(token);
            await stream.WriteAsync(bytes.AsMemory(Prefix.Length), token);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Disposed = true;
            base.Dispose(disposing);
        }
    }

    internal sealed class CallbackHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            => send(request, token);
    }

    internal sealed record LogEntry(string Message, IReadOnlyDictionary<string, object?> Fields, Func<string> Render);

    internal sealed class CapturingLogger<T> : ILogger<T>
    {
        public bool Enabled { get; set; } = true;
        public List<LogEntry> Entries { get; } = [];
        public int ThrowOnEntry { get; set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => Enabled;

        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!Enabled) return;
            if (ThrowOnEntry == Entries.Count + 1) throw new InvalidOperationException("Logger failed.");
            var fields = ((IEnumerable<KeyValuePair<string, object?>>)(object)state!)
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            Entries.Add(new(formatter(state, exception), fields, () => formatter(state, exception)));
        }
    }
}
