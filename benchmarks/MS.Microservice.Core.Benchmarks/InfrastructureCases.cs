using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using MS.Microservice.Core.Net.Http;
using MS.Microservice.Core.Security.Cryptology;
using MS.Microservice.Core.Serialization;

internal static class InfrastructureCases
{
    public static void Run()
    {
        Bench.Measure("ChineseEncoder getter", 64, () => DefaultSerializeSetting.ChineseEncoder,
            encoder => !encoder.WillEncode('中') && encoder.WillEncode('<'));

        var handler = new SinkHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var logger = NullLogger<LogHttpClient>.Instance;
        Bench.Measure("LogHttpClient construction", 64, () => new LogHttpClient(logger, http),
            client => client.JsonSerializerOptions.PropertyNameCaseInsensitive && !client.JsonSerializerOptions.Encoder!.WillEncode('中'));

        var client = new LogHttpClient(logger, http);
        foreach (int repeats in new[] { 1, 8192 })
        {
            var payload = new { Message = string.Concat(Enumerable.Repeat("A中&", repeats)) };
            int expectedLength = JsonSerializer.SerializeToUtf8Bytes(payload, client.JsonSerializerOptions).Length;
            Bench.Measure($"POST JSON {expectedLength} bytes", repeats == 1 ? 512 : 32,
                () => client.PostAsync<int>("data", payload).GetAwaiter().GetResult(),
                result => result == 1 && handler.Length == expectedLength);
        }

        Bench.Measure("SHA256 UTF-8 string", 2048, () => CryptologyHelper.SHA256("订单🔐"),
            hash => hash == "23862f7e75ed64a25d20c9e674d0979dcbbba98e3758c6f1dd118e9a809d74e8");
    }

    private sealed class SinkHandler : HttpMessageHandler
    {
        private static readonly byte[] Response = "1"u8.ToArray();
        public long? Length { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = request.Content!;
            Length = content.Headers.ContentLength;
            await content.CopyToAsync(Stream.Null, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Response) };
        }
    }
}
