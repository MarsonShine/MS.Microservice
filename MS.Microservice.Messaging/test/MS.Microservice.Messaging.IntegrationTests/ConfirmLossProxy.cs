using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace MS.Microservice.Messaging.IntegrationTests;

/// <summary>A fixture-only AMQP proxy that observes a real publisher confirm and withholds it.</summary>
internal sealed class ConfirmLossProxy(string upstreamHost, int upstreamPort) : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stop = new();
    private int disposed;
    private Task? session;
    private TcpClient? downstream, upstream;
    public TaskCompletionSource ConfirmObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    public void Start()
    {
        listener.Start();
        session = ForwardAsync();
    }

    private async Task ForwardAsync()
    {
        try
        {
            downstream = await listener.AcceptTcpClientAsync(stop.Token);
            upstream = new TcpClient();
            await upstream.ConnectAsync(upstreamHost, upstreamPort, stop.Token);
            var clientToBroker = downstream.GetStream().CopyToAsync(upstream.GetStream(), stop.Token);
            var brokerToClient = ReadFramesAsync(upstream.GetStream(), downstream.GetStream(), stop.Token);
            await Task.WhenAny(clientToBroker, brokerToClient);
            downstream.Dispose();
            upstream.Dispose();
            await Task.WhenAll(clientToBroker, brokerToClient);
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
        {
            if (!stop.IsCancellationRequested) ConfirmObserved.TrySetException(exception);
        }
    }

    private async Task ReadFramesAsync(Stream broker, Stream client, CancellationToken token)
    {
        var header = new byte[7];
        while (true)
        {
            await broker.ReadExactlyAsync(header, token);
            var size = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(3, 4));
            if (size > 16 * 1024 * 1024) throw new IOException("Unexpected AMQP frame size.");
            var frame = new byte[checked((int)size + 1)];
            await broker.ReadExactlyAsync(frame, token);
            if (header[0] == 1 && size >= 4
                && BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(0, 2)) == 60
                && BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(2, 2)) == 80)
            {
                ConfirmObserved.TrySetResult();
                // The test cuts the connection after this signal. No confirm reaches the publisher.
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            await client.WriteAsync(header, token);
            await client.WriteAsync(frame, token);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        stop.Cancel();
        listener.Stop();
        downstream?.Dispose();
        upstream?.Dispose();
        if (session is not null) await session;
        stop.Dispose();
    }
}
