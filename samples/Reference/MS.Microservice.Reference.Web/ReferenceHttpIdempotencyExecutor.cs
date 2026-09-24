using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using MS.Microservice.AspNetCore;
using MS.Microservice.Idempotency.EFCore;
using MS.Microservice.Messaging;
using MS.Microservice.Reference.Persistence;

namespace MS.Microservice.Reference.Web;

internal sealed class ReferenceHttpIdempotencyExecutor(
    EfCoreIdempotencyStore<ReferenceDbContext> store,
    IUnitOfWork unit,
    IServiceScopeFactory scopes,
    ExternalIdentityOptions identity)
{
    internal const string HeaderName = "Idempotency-Key";
    private static readonly TimeSpan Retention = TimeSpan.FromHours(24);
    internal static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);

    // Returns whether the endpoint delegate ran. MVC needs this to distinguish a short-circuited resource filter.
    internal async Task<bool> ExecuteAsync(HttpContext http, string operation, Func<Task> invokeEndpoint)
    {
        var values = http.Request.Headers[HeaderName];
        if (values.Count != 1 || values[0] is not { } key)
        {
            await WriteInvalidKeyAsync(http);
            return false;
        }

        var actor = ProfileEndpoints.Actor(http.User, identity);
        if (string.IsNullOrEmpty(actor.Issuer) || string.IsNullOrEmpty(actor.Subject))
        {
            http.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return false;
        }
        var actorBytes = JsonSerializer.SerializeToUtf8Bytes(actor, ReferenceIdempotencyJsonContext.Default.AuditActor);
        var actorHash = Convert.ToHexString(SHA256.HashData(actorBytes));

        if (http.Request.ContentLength > IdempotencyRequest.MaximumRequestBytes)
        {
            http.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return false;
        }
        var originalRequestBody = http.Request.Body;
        await using var body = new MemoryStream(http.Request.ContentLength is { } length
            ? (int)length : 0);
        if (!await ReadBodyAsync(http.Request, body, http.RequestAborted))
        {
            CryptographicOperations.ZeroMemory(body.GetBuffer().AsSpan(0, checked((int)body.Length)));
            http.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return false;
        }

        var bodyBytes = body.GetBuffer().AsMemory(0, checked((int)body.Length));
        IdempotencyRequest request;
        try
        {
            request = IdempotencyRequest.Create(operation, actorHash, key,
                Fingerprint(http.Request, bodyBytes));
        }
        catch (JsonException)
        {
            CryptographicOperations.ZeroMemory(bodyBytes.Span);
            await ApplicationErrorResults.ToProblem("validation", "Request body is invalid.").ExecuteAsync(http);
            return false;
        }
        catch (ArgumentException)
        {
            CryptographicOperations.ZeroMemory(bodyBytes.Span);
            await WriteInvalidKeyAsync(http);
            return false;
        }

        body.Position = 0;
        http.Request.Body = body;
        try
        {
            var existing = await FindWithLegacyProfileAsync(store, request, operation, actorHash, key,
                http.Request.ContentType, bodyBytes, http.RequestAborted);
            if (existing.Kind == IdempotencyLookupKind.Replay)
            {
                await WriteStoredAsync(http, existing.Response!);
                return false;
            }
            if (existing.Kind == IdempotencyLookupKind.DifferentRequest)
            {
                await WriteDifferentRequestAsync(http);
                return false;
            }

            var originalStatus = http.Response.StatusCode;
            var originalHeaders = http.Response.Headers.ToArray();
            var invoked = false;
            try
            {
                var response = await unit.ExecuteAsync(token => store.ClaimAndExecuteAsync(request, Retention,
                    async _ =>
                    {
                        invoked = true;
                        var captured = await CaptureAsync(http, invokeEndpoint);
                        if (captured.StatusCode is < 200 or >= 300)
                            throw new NonSuccessfulResponse(captured);
                        return new IdempotencyResponse(captured.StatusCode,
                            captured.ContentType, captured.Body, captured.Location);
                    }, token), http.RequestAborted);
                await WriteStoredAsync(http, response);
                return invoked;
            }
            catch (NonSuccessfulResponse rejected)
            {
                await WriteCapturedAsync(http, rejected.Response);
                return invoked;
            }
            catch (DbUpdateException)
            {
                RestoreResponse(http, originalStatus, originalHeaders);
                await using var scope = scopes.CreateAsyncScope();
                var freshStore = scope.ServiceProvider.GetRequiredService<EfCoreIdempotencyStore<ReferenceDbContext>>();
                var winner = await FindWithLegacyProfileAsync(freshStore, request, operation, actorHash, key,
                    http.Request.ContentType, bodyBytes, http.RequestAborted);
                if (winner.Kind == IdempotencyLookupKind.Replay)
                {
                    await WriteStoredAsync(http, winner.Response!);
                    return invoked;
                }
                if (winner.Kind == IdempotencyLookupKind.DifferentRequest)
                {
                    await WriteDifferentRequestAsync(http);
                    return invoked;
                }
                throw;
            }
            catch
            {
                RestoreResponse(http, originalStatus, originalHeaders);
                throw;
            }
        }
        finally
        {
            http.Request.Body = originalRequestBody;
            CryptographicOperations.ZeroMemory(body.GetBuffer().AsSpan(0, checked((int)body.Length)));
        }
    }

    private static async Task<bool> ReadBodyAsync(HttpRequest request, MemoryStream destination, CancellationToken token)
    {
        if (request.ContentLength > IdempotencyRequest.MaximumRequestBytes) return false;
        var rented = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            int count;
            while ((count = await request.Body.ReadAsync(rented.AsMemory(), token)) != 0)
            {
                if (destination.Length + count > IdempotencyRequest.MaximumRequestBytes) return false;
                await destination.WriteAsync(rented.AsMemory(0, count), token);
            }
            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented, clearArray: true);
        }
    }

    private static async Task<IdempotencyLookup> FindWithLegacyProfileAsync(
        EfCoreIdempotencyStore<ReferenceDbContext> store, IdempotencyRequest request,
        string operation, string actorHash, string key, string? contentType,
        ReadOnlyMemory<byte> body, CancellationToken token)
    {
        var found = await store.FindAsync(request, token);
        if (found.Kind != IdempotencyLookupKind.DifferentRequest ||
            operation != "profiles.create" || !IsJson(MediaType(contentType))) return found;

        // Records created before the shared executor used the bound CreateProfile DTO as their fingerprint.
        // Keep replaying those records during their 24-hour retention window.
        try
        {
            using var document = ParseJson(body, contentType);
            var profile = document.RootElement.Deserialize(ReferenceIdempotencyJsonContext.Default.CreateProfile);
            if (profile is null) return found;
            var legacyBody = JsonSerializer.SerializeToUtf8Bytes(profile,
                ReferenceIdempotencyJsonContext.Default.CreateProfile);
            try
            {
                var legacyRequest = IdempotencyRequest.Create(operation, actorHash, key, legacyBody);
                var legacy = await store.FindAsync(legacyRequest, token);
                return legacy.Kind == IdempotencyLookupKind.Replay ? legacy : found;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(legacyBody);
            }
        }
        catch (JsonException)
        {
            return found;
        }
    }

    private static byte[] Fingerprint(HttpRequest request, ReadOnlyMemory<byte> body)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendField(hash, Encoding.UTF8.GetBytes(request.Method));
        AppendField(hash, Encoding.UTF8.GetBytes(request.Path.Value ?? ""));
        AppendField(hash, Encoding.UTF8.GetBytes(request.QueryString.Value ?? ""));
        var mediaType = MediaType(request.ContentType);
        var isJson = IsJson(mediaType);
        AppendField(hash, Encoding.UTF8.GetBytes(request.ContentType ?? ""));
        if (isJson)
        {
            using var document = ParseJson(body, request.ContentType);
            var canonical = new ArrayBufferWriter<byte>(Math.Max(256, body.Length));
            try
            {
                using var writer = new Utf8JsonWriter(canonical);
                WriteCanonicalJson(writer, document.RootElement);
                writer.Flush();
                AppendField(hash, canonical.WrittenSpan);
            }
            finally
            {
                canonical.Clear();
            }
        }
        else AppendField(hash, body.Span);
        return hash.GetHashAndReset();
    }

    private static ReadOnlySpan<char> MediaType(string? contentType)
    {
        var mediaType = contentType.AsSpan();
        var separator = mediaType.IndexOf(';');
        if (separator >= 0) mediaType = mediaType[..separator];
        return mediaType.Trim();
    }

    private static bool IsJson(ReadOnlySpan<char> mediaType)
    {
        return mediaType.Equals("application/json".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            mediaType.EndsWith("+json".AsSpan(), StringComparison.OrdinalIgnoreCase);
    }

    private static JsonDocument ParseJson(ReadOnlyMemory<byte> body, string? contentType)
    {
        if (System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(contentType, out var mediaType) &&
            string.Equals(mediaType.CharSet, "utf-16", StringComparison.OrdinalIgnoreCase))
        {
            var json = Encoding.Unicode.GetString(body.Span).TrimStart('\uFEFF');
            return JsonDocument.Parse(json);
        }
        return JsonDocument.Parse(body);
    }

    private static void WriteCanonicalJson(Utf8JsonWriter writer, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var properties = element.EnumerateObject().ToArray();
            Array.Sort(properties, static (left, right) =>
            {
                var folded = StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
                return folded != 0 ? folded : StringComparer.Ordinal.Compare(left.Name, right.Name);
            });
            writer.WriteStartObject();
            for (var index = 0; index < properties.Length; index++)
            {
                if (index > 0 && string.Equals(properties[index - 1].Name, properties[index].Name,
                    StringComparison.OrdinalIgnoreCase))
                    throw new JsonException("Duplicate JSON property names are not supported.");
                writer.WritePropertyName(properties[index].Name);
                WriteCanonicalJson(writer, properties[index].Value);
            }
            writer.WriteEndObject();
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in element.EnumerateArray()) WriteCanonicalJson(writer, item);
            writer.WriteEndArray();
        }
        else element.WriteTo(writer);
    }

    private static void AppendField(IncrementalHash hash, ReadOnlySpan<byte> value)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, value.Length);
        hash.AppendData(length);
        hash.AppendData(value);
    }

    private static async Task<CapturedResponse> CaptureAsync(HttpContext http, Func<Task> invokeEndpoint)
    {
        var originalBody = http.Response.Body;
        await using var output = new BoundedResponseStream();
        http.Response.Body = output;
        try
        {
            await invokeEndpoint();
            var location = http.Response.Headers.Location.ToString();
            return new CapturedResponse(http.Response.StatusCode, http.Response.ContentType,
                string.IsNullOrEmpty(location) ? null : location, output.ToArray());
        }
        finally
        {
            http.Response.Body = originalBody;
        }
    }

    private static Task WriteInvalidKeyAsync(HttpContext http)
        => ApplicationErrorResults.ToProblem("validation", "Idempotency-Key or request representation is invalid.")
            .ExecuteAsync(http);

    private static Task WriteDifferentRequestAsync(HttpContext http)
        => ApplicationErrorResults.ToProblem("conflict", "Idempotency-Key was used for a different request.")
            .ExecuteAsync(http);

    private static async Task WriteStoredAsync(HttpContext http, IdempotencyResponse response)
    {
        http.Response.StatusCode = response.StatusCode;
        http.Response.ContentType = response.ContentType;
        if (response.Location is { } location) http.Response.Headers.Location = location;
        await http.Response.Body.WriteAsync(response.Body, http.RequestAborted);
    }

    private static async Task WriteCapturedAsync(HttpContext http, CapturedResponse response)
    {
        http.Response.StatusCode = response.StatusCode;
        if (response.ContentType is { } contentType) http.Response.ContentType = contentType;
        if (!string.IsNullOrEmpty(response.Location)) http.Response.Headers.Location = response.Location;
        await http.Response.Body.WriteAsync(response.Body, http.RequestAborted);
    }

    private static void RestoreResponse(HttpContext http, int status,
        KeyValuePair<string, StringValues>[] headers)
    {
        http.Response.StatusCode = status;
        http.Response.Headers.Clear();
        foreach (var header in headers) http.Response.Headers[header.Key] = header.Value;
    }

    private readonly record struct CapturedResponse(int StatusCode, string? ContentType, string? Location, byte[] Body);
    private sealed class NonSuccessfulResponse(CapturedResponse response) : Exception
    {
        internal CapturedResponse Response { get; } = response;
    }

    private sealed class BoundedResponseStream : MemoryStream
    {
        private void EnsureCapacityFor(int count)
        {
            if (Position + count > IdempotencyResponse.MaximumBodyBytes)
                throw new InvalidOperationException("Idempotent response exceeds the stored body limit.");
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureCapacityFor(count);
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            EnsureCapacityFor(buffer.Length);
            base.Write(buffer);
        }

        public override void WriteByte(byte value)
        {
            EnsureCapacityFor(1);
            base.WriteByte(value);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            EnsureCapacityFor(count);
            return base.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            EnsureCapacityFor(buffer.Length);
            return base.WriteAsync(buffer, cancellationToken);
        }

        public override void SetLength(long value)
        {
            if (value > IdempotencyResponse.MaximumBodyBytes)
                throw new InvalidOperationException("Idempotent response exceeds the stored body limit.");
            base.SetLength(value);
        }
    }
}
