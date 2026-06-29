using MS.Microservice.Infrastructure.FileSystem;
using System.ComponentModel;
using Xunit;

namespace MS.Microservice.Infrastructure.Tests.FileSystem;

public sealed class NetworkShareAccessorTests
{
    private const string NetworkPath = @"\\server\share";
    private const string ServerPath = @"\\server";
    private const string UserName = @"DOMAIN\user";
    private const string Password = "pass";

    [Fact]
    public void Constructor_NullNetworkName_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new NetworkShareAccessor(null!, UserName, Password));
    }

    [Fact]
    public void Constructor_EmptyNetworkName_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            new NetworkShareAccessor("", UserName, Password));
    }

    [Fact]
    public void Constructor_NullUserName_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new NetworkShareAccessor(NetworkPath, null!, Password));
    }

    [Fact]
    public void Constructor_EmptyUserName_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            new NetworkShareAccessor(NetworkPath, "", Password));
    }

    [Fact]
    public void Constructor_AccessibleShare_UsesFastPath()
    {
        var recorder = new RecordingPlatform
        {
            DirectoryExists = _ => true
        };

        using var accessor = new NetworkShareAccessor(NetworkPath, UserName, Password, forceConnect: false, recorder.Build());

        Assert.False(accessor.DidEstablishConnection);
        Assert.Empty(recorder.ConnectCalls);
        Assert.Empty(recorder.CancelCalls);
    }

    [Fact]
    public void Constructor_ForceConnect_SkipsAccessibilityCheck()
    {
        int directoryChecks = 0;
        var recorder = new RecordingPlatform
        {
            DirectoryExists = _ =>
            {
                directoryChecks++;
                return true;
            }
        };

        using var accessor = new NetworkShareAccessor(NetworkPath, UserName, Password, forceConnect: true, recorder.Build());

        Assert.True(accessor.DidEstablishConnection);
        Assert.Equal(0, directoryChecks);
        Assert.Single(recorder.ConnectCalls);
    }

    [Fact]
    public void Constructor_DirectoryCheckThrows_FallsBackToConnection()
    {
        var recorder = new RecordingPlatform
        {
            DirectoryExists = _ => throw new IOException("boom")
        };

        using var accessor = new NetworkShareAccessor(NetworkPath, UserName, Password, forceConnect: false, recorder.Build());

        Assert.True(accessor.DidEstablishConnection);
        Assert.Single(recorder.ConnectCalls);
    }

    [Fact]
    public void Constructor_InaccessibleShare_DisconnectsShareAndServerBeforeConnecting()
    {
        var recorder = new RecordingPlatform();
        var accessor = new NetworkShareAccessor(NetworkPath, UserName, Password, forceConnect: false, recorder.Build());

        accessor.Dispose();

        Assert.True(accessor.DidEstablishConnection);
        Assert.Single(recorder.ConnectCalls);
        Assert.Collection(
            recorder.CancelCalls,
            call => Assert.Equal(new CancelCall(NetworkPath, 0x00000001, true), call),
            call => Assert.Equal(new CancelCall(ServerPath, 0x00000001, true), call),
            call => Assert.Equal(new CancelCall(NetworkPath, 0x00000001, true), call));
    }

    [Fact]
    public void Constructor_DisconnectFallback_UsesNonProfileFlagWhenNeeded()
    {
        bool allowProfileDisconnect = false;
        var recorder = new RecordingPlatform
        {
            CancelConnection = (_, flags, _) => flags == 0 || allowProfileDisconnect
        };

        var accessor = new NetworkShareAccessor(NetworkPath, UserName, Password, forceConnect: false, recorder.Build());

        Assert.Collection(
            recorder.CancelCalls.Take(4),
            call => Assert.Equal(new CancelCall(NetworkPath, 0x00000001, true), call),
            call => Assert.Equal(new CancelCall(NetworkPath, 0, true), call),
            call => Assert.Equal(new CancelCall(ServerPath, 0x00000001, true), call),
            call => Assert.Equal(new CancelCall(ServerPath, 0, true), call));

        allowProfileDisconnect = true;
        accessor.Dispose();
    }

    [Fact]
    public void Constructor_Win32Error1219_RetriesThreeTimesBeforeThrowing()
    {
        var recorder = new RecordingPlatform
        {
            UseConnection = (_, _, _) => 1219
        };

        var exception = Assert.Throws<Win32Exception>(() =>
            new NetworkShareAccessor(NetworkPath, UserName, Password, forceConnect: false, recorder.Build()));

        Assert.Equal(1219, exception.NativeErrorCode);
        Assert.Equal(3, recorder.ConnectCalls.Count);
        Assert.Collection(
            recorder.SleepCalls,
            delay => Assert.Equal(TimeSpan.FromMilliseconds(500), delay),
            delay => Assert.Equal(TimeSpan.FromMilliseconds(1000), delay));
    }

    [Fact]
    public void Constructor_Non1219Error_ThrowsImmediately()
    {
        var recorder = new RecordingPlatform
        {
            UseConnection = (_, _, _) => 1326
        };

        var exception = Assert.Throws<Win32Exception>(() =>
            new NetworkShareAccessor(NetworkPath, UserName, Password, forceConnect: false, recorder.Build()));

        Assert.Equal(1326, exception.NativeErrorCode);
        Assert.Single(recorder.ConnectCalls);
        Assert.Empty(recorder.SleepCalls);
    }

    [Fact]
    public void Dispose_MultipleCalls_CancelsOnlyOnce()
    {
        var recorder = new RecordingPlatform();
        var accessor = new NetworkShareAccessor(NetworkPath, UserName, Password, forceConnect: false, recorder.Build());
        int cancelCallsBeforeDispose = recorder.CancelCalls.Count;

        accessor.Dispose();
        accessor.Dispose();

        Assert.Equal(cancelCallsBeforeDispose + 1, recorder.CancelCalls.Count);
        Assert.Equal(new CancelCall(NetworkPath, 0x00000001, true), recorder.CancelCalls[^1]);
    }

    [Theory]
    [InlineData(@"\\192.168.1.2\工作目录", @"\\192.168.1.2")]
    [InlineData(@"\\server\share\subdir", @"\\server")]
    [InlineData(@"\\localhost\c$", @"\\localhost")]
    [InlineData(@"\\10.0.0.1\data", @"\\10.0.0.1")]
    [InlineData(@"\\SERVER", @"\\SERVER")]
    [InlineData(@"C:\local\path", null)]
    [InlineData(@"not-a-network-path", null)]
    public void ExtractServerPath_VariousPaths_ReturnsExpected(string input, string? expected)
    {
        string? result = NetworkShareAccessor.ExtractServerPath(input);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Execute_ReturnsDelegateResult()
    {
        var recorder = new RecordingPlatform();

        int result = NetworkShareAccessor.Execute(
            NetworkPath,
            UserName,
            Password,
            () => 42,
            platform: recorder.Build());

        Assert.Equal(42, result);
        Assert.Single(recorder.ConnectCalls);
        Assert.Equal(new CancelCall(NetworkPath, 0x00000001, true), recorder.CancelCalls[^1]);
    }

    [Fact]
    public void Execute_WhenActionThrows_StillDisposesAccessor()
    {
        var recorder = new RecordingPlatform();

        Assert.Throws<InvalidOperationException>(() =>
            NetworkShareAccessor.Execute(
                NetworkPath,
                UserName,
                Password,
                () => throw new InvalidOperationException("boom"),
                platform: recorder.Build()));

        Assert.Equal(new CancelCall(NetworkPath, 0x00000001, true), recorder.CancelCalls[^1]);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsDelegateResult()
    {
        var recorder = new RecordingPlatform();

        int result = await NetworkShareAccessor.ExecuteAsync(
            NetworkPath,
            UserName,
            Password,
            async () =>
            {
                await Task.Yield();
                return 24;
            },
            platform: recorder.Build());

        Assert.Equal(24, result);
        Assert.Single(recorder.ConnectCalls);
        Assert.Equal(new CancelCall(NetworkPath, 0x00000001, true), recorder.CancelCalls[^1]);
    }

    [Fact]
    public async Task ExecuteAsync_WhenActionThrows_StillDisposesAccessor()
    {
        var recorder = new RecordingPlatform();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NetworkShareAccessor.ExecuteAsync(
                NetworkPath,
                UserName,
                Password,
                async () =>
                {
                    await Task.Yield();
                    throw new InvalidOperationException("boom");
                },
                platform: recorder.Build()));

        Assert.Equal(new CancelCall(NetworkPath, 0x00000001, true), recorder.CancelCalls[^1]);
    }

    private sealed class RecordingPlatform
    {
        public List<CancelCall> CancelCalls { get; } = [];
        public List<ConnectCall> ConnectCalls { get; } = [];
        public List<TimeSpan> SleepCalls { get; } = [];

        public Func<string, bool> DirectoryExists { get; set; } = _ => false;
        public Func<string, string, string, int> UseConnection { get; set; } = (_, _, _) => 0;
        public Func<string, int, bool, bool> CancelConnection { get; set; } = (_, _, _) => true;

        public NetworkShareAccessor.Platform Build()
            => new()
            {
                DirectoryExists = path => DirectoryExists(path),
                UseConnection = (networkName, userName, password) =>
                {
                    ConnectCalls.Add(new ConnectCall(networkName, userName, password));
                    return UseConnection(networkName, userName, password);
                },
                CancelConnection = (path, flags, force) =>
                {
                    CancelCalls.Add(new CancelCall(path, flags, force));
                    return CancelConnection(path, flags, force);
                },
                Sleep = delay => SleepCalls.Add(delay)
            };
    }

    private readonly record struct ConnectCall(string NetworkName, string UserName, string Password);

    private readonly record struct CancelCall(string Path, int Flags, bool Force);
}
