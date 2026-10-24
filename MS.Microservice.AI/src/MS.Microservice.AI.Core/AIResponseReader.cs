using System.Text;
using MS.Microservice.AI.Abstractions;

namespace MS.Microservice.AI.Core;

internal static class AIResponseReader
{
    public static async Task BufferAsync(HttpResponseMessage response, AIProviderRegistrationOptions options,
        string provider, AICapability capability, AIResolvedModel model, string? requestId,
        CancellationToken cancellationToken)
    {
        var maximum = response.IsSuccessStatusCode ? options.MaxResponseBytes : options.MaxErrorResponseBytes;
        try
        {
            await response.Content.LoadIntoBufferAsync(maximum, cancellationToken).ConfigureAwait(false);
            // LoadIntoBufferAsync skips the limit for content that a handler already buffered.
            // Copying that buffer to a counting sink verifies actual bytes without a second payload allocation.
            using var counter = new ByteLimitSink(maximum);
            await response.Content.CopyToAsync(counter, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception) when (exception.HttpRequestError == HttpRequestError.ConfigurationLimitExceeded)
        {
            throw LimitExceeded(provider, capability, model, requestId, response,
                AIProviderErrors.GetProviderRequestId(response), "response byte", exception);
        }
    }

    public static AIProviderException LimitExceeded(string provider, AICapability capability, AIResolvedModel model,
        string? requestId, HttpResponseMessage response, string? providerRequestId, string budget, Exception exception)
        => new($"AI provider '{provider}' exceeded its configured {budget} limit.", AIErrorCodes.ResponseInvalid,
            capability, provider: provider, model: model.Model, scenario: model.Scenario, requestId: requestId,
            providerRequestId: providerRequestId, statusCode: (int)response.StatusCode,
            isTransient: false, innerException: exception);

    internal sealed class BoundedLineReader(StreamReader reader, int maximumCharacters)
    {
        private readonly char[] _character = new char[1];
        private bool _skipLineFeed;

        public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            var line = new StringBuilder(Math.Min(256, maximumCharacters), maximumCharacters);
            while (await reader.ReadAsync(_character.AsMemory(), cancellationToken).ConfigureAwait(false) != 0)
            {
                var character = _character[0];
                if (_skipLineFeed)
                {
                    _skipLineFeed = false;
                    if (character == '\n') continue;
                }
                if (character == '\r')
                {
                    // Return immediately at CR; waiting for a possible LF would delay a complete event.
                    _skipLineFeed = true;
                    return line.ToString();
                }
                if (character == '\n') return line.ToString();
                if (line.Length == maximumCharacters)
                    throw new HttpRequestException(HttpRequestError.ConfigurationLimitExceeded,
                        "The streaming response line exceeds its configured character limit.");
                line.Append(character);
            }
            return line.Length == 0 ? null : line.ToString();
        }
    }

    private sealed class ByteLimitSink(long maximumBytes) : Stream
    {
        private long _remaining = maximumBytes;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length > _remaining)
                throw new HttpRequestException(HttpRequestError.ConfigurationLimitExceeded,
                    "The buffered response exceeds its configured byte limit.");
            _remaining -= buffer.Length;
        }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Write(buffer, offset, count);
            return Task.CompletedTask;
        }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Write(buffer.Span);
            return ValueTask.CompletedTask;
        }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
