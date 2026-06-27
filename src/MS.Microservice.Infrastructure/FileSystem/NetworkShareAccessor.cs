using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

[assembly: InternalsVisibleTo("MS.Microservice.Infrastructure.Tests")]

namespace MS.Microservice.Infrastructure.FileSystem;

/// <summary>
/// 网络共享连接访问器。
///
/// 策略：
/// 1. 先检查共享是否已经可访问（利用当前用户会话的已有连接），如果可访问则跳过连接。
/// 2. 如果不可访问，先尽力断开已有连接，再用 WNetUseConnection 建立临时连接。
/// 3. 处理 1219 错误：重试断开 + 等待后重连。
///
/// 使用方式：
/// <code>
/// var exists = NetworkShareAccessor.Execute(
///     @"\\server\share",
///     "user",
///     "pass",
///     () => File.Exists(@"\\server\share\somefile.txt"));
/// </code>
///
/// 需要跨多步访问时，仍可使用构造函数配合 using 控制作用域。
/// </summary>
public sealed partial class NetworkShareAccessor : IDisposable
{
    private static readonly Lock ConnectLock = new();
    private static readonly Platform DefaultPlatform = new();

    private readonly string _networkName;
    private readonly Platform _platform;
    private readonly bool _didConnect;
    private bool _disposed;

    /// <summary>
    /// 是否实际通过 WNetUseConnection 建立了新连接。
    /// false 表示 fast-path（共享已可访问，无需连接）。
    /// 可用于测试断言。
    /// </summary>
    internal bool DidEstablishConnection => _didConnect;

    /// <summary>
    /// 在访问共享路径前临时建立连接，并在委托返回后自动断开。
    /// </summary>
    public static void Execute(string networkName, string userName, string password, Action action)
        => Execute(networkName, userName, password, action, platform: null);

    internal static void Execute(string networkName, string userName, string password, Action action, Platform? platform)
    {
        ArgumentNullException.ThrowIfNull(action);

        using var accessor = new NetworkShareAccessor(networkName, userName, password, forceConnect: false, platform);
        action();
    }

    /// <summary>
    /// 在访问共享路径前临时建立连接，并返回委托结果。
    /// </summary>
    public static TResult Execute<TResult>(string networkName, string userName, string password, Func<TResult> action)
        => Execute(networkName, userName, password, action, platform: null);

    internal static TResult Execute<TResult>(string networkName, string userName, string password, Func<TResult> action, Platform? platform)
    {
        ArgumentNullException.ThrowIfNull(action);

        using var accessor = new NetworkShareAccessor(networkName, userName, password, forceConnect: false, platform);
        return action();
    }

    /// <summary>
    /// 在访问共享路径前临时建立连接，并等待异步委托完成后自动断开。
    /// </summary>
    public static Task ExecuteAsync(string networkName, string userName, string password, Func<Task> action)
        => ExecuteAsync(networkName, userName, password, action, platform: null);

    internal static async Task ExecuteAsync(string networkName, string userName, string password, Func<Task> action, Platform? platform)
    {
        ArgumentNullException.ThrowIfNull(action);

        using var accessor = new NetworkShareAccessor(networkName, userName, password, forceConnect: false, platform);
        await action().ConfigureAwait(false);
    }

    /// <summary>
    /// 在访问共享路径前临时建立连接，并返回异步委托结果。
    /// </summary>
    public static Task<TResult> ExecuteAsync<TResult>(string networkName, string userName, string password, Func<Task<TResult>> action)
        => ExecuteAsync(networkName, userName, password, action, platform: null);

    internal static async Task<TResult> ExecuteAsync<TResult>(string networkName, string userName, string password, Func<Task<TResult>> action, Platform? platform)
    {
        ArgumentNullException.ThrowIfNull(action);

        using var accessor = new NetworkShareAccessor(networkName, userName, password, forceConnect: false, platform);
        return await action().ConfigureAwait(false);
    }

    /// <summary>
    /// 连接到指定的网络共享。
    /// </summary>
    /// <param name="networkName">共享路径，如 \\server\share</param>
    /// <param name="userName">用户名（建议包含域名，如 DOMAIN\user 或 user@domain.com）</param>
    /// <param name="password">密码</param>
    /// <exception cref="Win32Exception">连接失败时抛出，NativeErrorCode 为 Win32 错误码</exception>
    public NetworkShareAccessor(string networkName, string userName, string password)
        : this(networkName, userName, password, forceConnect: false, platform: null)
    {
    }

    /// <summary>
    /// 强制走真实连接路径，跳过"共享是否已可访问"的快速检查。
    /// </summary>
    internal NetworkShareAccessor(string networkName, string userName, string password, bool forceConnect)
        : this(networkName, userName, password, forceConnect, platform: null)
    {
    }

    internal NetworkShareAccessor(string networkName, string userName, string password, bool forceConnect, Platform? platform)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(networkName);
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);

        _networkName = networkName;
        _platform = platform ?? DefaultPlatform;

        lock (ConnectLock)
        {
            // 策略 1：检查共享是否已经可访问（利用当前用户会话的已有连接）
            // forceConnect=true 时跳过此检查，强制走 WNetUseConnection 路径
            if (!forceConnect && IsShareAlreadyAccessible(networkName, _platform.DirectoryExists))
            {
                _didConnect = false;
                return;
            }

            // 策略 2：共享不可访问，需要建立新连接
            // 先尽力断开已有的冲突连接
            ForceDisconnectServerConnections(networkName);

            // 尝试连接（使用 WNetUseConnection，它对凭据冲突的处理优于 WNetAddConnection2）
            TryConnect(networkName, userName, password);
            _didConnect = true;
        }
    }

    /// <summary>
    /// 断开连接，释放资源。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (!_didConnect)
            return;

        lock (ConnectLock)
        {
            _platform.CancelConnection(_networkName, ConnectUpdateProfile, force: true);
        }
    }

    /// <summary>
    /// 检查共享目录是否已经可以访问。
    /// 如果可以访问，说明当前用户会话已经有有效连接，无需重复建立。
    /// </summary>
    internal static bool IsShareAlreadyAccessible(string networkName)
        => IsShareAlreadyAccessible(networkName, static path => Directory.Exists(path));

    private static bool IsShareAlreadyAccessible(string networkName, Func<string, bool> directoryExists)
    {
        try
        {
            return directoryExists(networkName);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 尝试使用 WNetUseConnection 建立连接。
    /// 如果遇到 1219 错误，会进行重试（再次断开 + 等待 + 重连）。
    /// </summary>
    private void TryConnect(string networkName, string userName, string password)
    {
        const int maxRetries = 2;
        int lastError = 0;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            if (attempt > 0)
            {
                // 重试前：更激进地断开，并延长等待时间
                ForceDisconnectServerConnections(networkName);
                _platform.Sleep(TimeSpan.FromMilliseconds(500 * attempt));
            }

            int errorCode = _platform.UseConnection(networkName, userName, password);
            if (errorCode == 0)
                return;

            lastError = errorCode;

            // 只有 1219 才重试，其他错误直接抛出
            if (errorCode != 1219)
            {
                throw new Win32Exception(
                    errorCode,
                    $"连接共享目录失败：{networkName}，Win32Error={errorCode}，用户名={userName}");
            }
        }

        // 重试耗尽，仍然 1219
        throw new Win32Exception(
            lastError,
            $"连接共享目录失败（重试{maxRetries}次后仍然1219）：{networkName}，用户名={userName}。" +
            "请检查是否有其他程序正在使用不同的凭据访问该服务器。");
    }

    /// <summary>
    /// 强制断开与目标服务器相关的所有网络连接。
    /// 会先断开具体共享路径，再断开服务器级别的连接，
    /// 并进行多次重试。
    /// </summary>
    private void ForceDisconnectServerConnections(string networkName)
    {
        // 提取服务器路径（\\server\share → \\server）
        string? serverPath = ExtractServerPath(networkName);

        for (int attempt = 0; attempt < 3; attempt++)
        {
            bool allDisconnected = TryDisconnect(networkName);

            if (serverPath != null &&
                !string.Equals(serverPath, networkName, StringComparison.OrdinalIgnoreCase))
            {
                allDisconnected &= TryDisconnect(serverPath);
            }

            if (allDisconnected)
                break;

            _platform.Sleep(TimeSpan.FromMilliseconds(200));
        }
    }

    private bool TryDisconnect(string path)
        => _platform.CancelConnection(path, ConnectUpdateProfile, force: true)
            || _platform.CancelConnection(path, 0, force: true);

    /// <summary>
    /// 从网络路径中提取服务器路径。
    /// \\192.168.1.2\share folder → \\192.168.1.2
    /// </summary>
    internal static string? ExtractServerPath(string networkPath)
    {
        var match = Regex.Match(networkPath, @"^\\\\[^\\]+");
        return match.Success ? match.Value : null;
    }

    // ponytail: keep the test seam as delegates inside Infrastructure; add a wider abstraction only if another caller needs to swap the transport.
    internal sealed class Platform
    {
        public Func<string, bool> DirectoryExists { get; init; } = static networkName => Directory.Exists(networkName);
        public UseConnectionCallback UseConnection { get; init; } = UseConnectionCore;
        public CancelConnectionCallback CancelConnection { get; init; } = NativeMethods.WNetCancelConnection2;
        public Action<TimeSpan> Sleep { get; init; } = static delay => Thread.Sleep(delay);
    }

    internal delegate int UseConnectionCallback(string networkName, string userName, string password);

    internal delegate bool CancelConnectionCallback(string path, int flags, bool force);

    private static int UseConnectionCore(string networkName, string userName, string password)
    {
        IntPtr remoteNamePtr = IntPtr.Zero;

        try
        {
            remoteNamePtr = Marshal.StringToHGlobalUni(networkName);

            var netResource = new NETRESOURCE
            {
                dwType = RESOURCETYPE_DISK,
                lpRemoteName = remoteNamePtr
            };

            int bufferSize = 256;
            var accessName = new StringBuilder(bufferSize);

            int result = NativeMethods.WNetUseConnection(
                IntPtr.Zero,
                ref netResource,
                password,
                userName,
                ConnectTemporary | ConnectRedirect,
                accessName,
                ref bufferSize,
                out int connectResult);

            if (result == 0)
                return 0;

            return result == ERROR_EXTENDED_ERROR ? connectResult : result;
        }
        finally
        {
            if (remoteNamePtr != IntPtr.Zero)
                Marshal.FreeHGlobal(remoteNamePtr);
        }
    }

    // ---- P/Invoke 定义 ----

    [StructLayout(LayoutKind.Sequential)]
    private struct NETRESOURCE
    {
        public int dwScope;
        public int dwType;
        public int dwDisplayType;
        public int dwUsage;
        public IntPtr lpLocalName;
        public IntPtr lpRemoteName;
        public IntPtr lpComment;
        public IntPtr lpProvider;
    }

    private static partial class NativeMethods
    {
        /// <summary>
        /// WNetUseConnection — 比 WNetAddConnection2 更智能的连接 API，
        /// 能更好地处理凭据冲突（1219 错误）。
        /// 使用传统 DllImport 而非 LibraryImport，因为需要 StringBuilder 输出参数。
        /// </summary>
        [DllImport("mpr.dll", EntryPoint = "WNetUseConnectionW", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern int WNetUseConnection(
            IntPtr hwndOwner,
            ref NETRESOURCE lpNetResource,
            string? lpPassword,
            string? lpUserId,
            int dwFlags,
            StringBuilder? lpAccessName,
            ref int lpBufferSize,
            out int lpResult);

        [LibraryImport("mpr.dll", EntryPoint = "WNetCancelConnection2W", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool WNetCancelConnection2(
            string lpName,
            int dwFlags,
            [MarshalAs(UnmanagedType.Bool)] bool force);
    }

    private const int RESOURCETYPE_DISK = 1;
    private const int ConnectUpdateProfile = 0x00000001;
    private const int ConnectTemporary = 0x00000004;
    private const int ConnectRedirect = 0x00000080;
    private const int ERROR_EXTENDED_ERROR = 1208; // WNet 扩展错误，connectResult 包含实际错误码
}
