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
/// using var accessor = new NetworkShareAccessor(@"\\server\share", "user", "pass");
/// // 在此作用域内可以访问共享文件夹中的文件
/// var exists = File.Exists(@"\\server\share\somefile.txt");
/// </code>
/// </summary>
public sealed partial class NetworkShareAccessor : IDisposable
{
    private static readonly Lock ConnectLock = new();

    private readonly string _networkName;
    private readonly bool _didConnect; // 是否实际建立了新连接（需要 Dispose 断开）
    private bool _disposed;

    /// <summary>
    /// 是否实际通过 WNetUseConnection 建立了新连接。
    /// false 表示 fast-path（共享已可访问，无需连接）。
    /// 可用于测试断言。
    /// </summary>
    internal bool DidEstablishConnection => _didConnect;

    /// <summary>
    /// 连接到指定的网络共享。
    /// </summary>
    /// <param name="networkName">共享路径，如 \\server\share</param>
    /// <param name="userName">用户名（建议包含域名，如 DOMAIN\user 或 user@domain.com）</param>
    /// <param name="password">密码</param>
    /// <exception cref="Win32Exception">连接失败时抛出，NativeErrorCode 为 Win32 错误码</exception>
    public NetworkShareAccessor(string networkName, string userName, string password)
        : this(networkName, userName, password, forceConnect: false)
    {
    }

    /// <summary>
    /// 强制走真实连接路径，跳过"共享是否已可访问"的快速检查。
    /// </summary>
    internal NetworkShareAccessor(string networkName, string userName, string password, bool forceConnect)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(networkName);
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);

        _networkName = networkName;

        lock (ConnectLock)
        {
            // 策略 1：检查共享是否已经可访问（利用当前用户会话的已有连接）
            // forceConnect=true 时跳过此检查，强制走 WNetUseConnection 路径
            if (!forceConnect && IsShareAlreadyAccessible(networkName))
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
            const int connectUpdateProfile = 0x00000001;
            NativeMethods.WNetCancelConnection2(_networkName, connectUpdateProfile, force: true);
        }
    }

    /// <summary>
    /// 检查共享目录是否已经可以访问。
    /// 如果可以访问，说明当前用户会话已经有有效连接，无需重复建立。
    /// </summary>
    internal static bool IsShareAlreadyAccessible(string networkName)
    {
        try
        {
            return Directory.Exists(networkName);
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
    private static void TryConnect(string networkName, string userName, string password)
    {
        const int maxRetries = 2;
        int lastError = 0;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            if (attempt > 0)
            {
                // 重试前：更激进地断开，并延长等待时间
                ForceDisconnectServerConnections(networkName);
                Thread.Sleep(500 * attempt); // 递增等待：500ms, 1000ms
            }

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

                // CONNECT_TEMPORARY (0x00000004)：不持久化
                // CONNECT_REDIRECT (0x00000080)：允许重定向处理凭据冲突
                const int flags = 0x00000004 | 0x00000080;

                int result = NativeMethods.WNetUseConnection(
                    IntPtr.Zero,
                    ref netResource,
                    password,
                    userName,
                    flags,
                    accessName,
                    ref bufferSize,
                    out int connectResult);

                // ⚠️ 关键：WNetUseConnection 成功时 result == 0，
                // connectResult 是附加信息（可能是 0、CONNECT_LOCALDRIVE=256、CONNECT_REDIRECT=128），
                // 这些全都表示成功，不是错误码！
                if (result == 0)
                    return; // 成功

                // result != 0 → 连接失败
                // 如果 result == ERROR_EXTENDED_ERROR (1208)，connectResult 包含真正的错误码
                int errorCode = result == ERROR_EXTENDED_ERROR ? connectResult : result;
                lastError = errorCode;

                // 只有 1219 才重试，其他错误直接抛出
                if (errorCode != 1219)
                {
                    throw new Win32Exception(
                        errorCode,
                        $"连接共享目录失败：{networkName}，Win32Error={errorCode}，用户名={userName}");
                }
            }
            finally
            {
                if (remoteNamePtr != IntPtr.Zero)
                    Marshal.FreeHGlobal(remoteNamePtr);
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
    private static void ForceDisconnectServerConnections(string networkName)
    {
        // 提取服务器路径（\\server\share → \\server）
        string? serverPath = ExtractServerPath(networkName);

        for (int attempt = 0; attempt < 3; attempt++)
        {
            bool allDisconnected = true;

            // 尝试多种方式断开
            // 方式1：带 CONNECT_UPDATE_PROFILE 标志
            if (!NativeMethods.WNetCancelConnection2(networkName, 0x00000001, force: true))
            {
                // 方式2：不带标志，纯强制断开
                if (!NativeMethods.WNetCancelConnection2(networkName, 0, force: true))
                {
                    allDisconnected = false;
                }
            }

            // 断开服务器级别连接
            if (serverPath != null &&
                !string.Equals(serverPath, networkName, StringComparison.OrdinalIgnoreCase))
            {
                if (!NativeMethods.WNetCancelConnection2(serverPath, 0x00000001, force: true))
                {
                    if (!NativeMethods.WNetCancelConnection2(serverPath, 0, force: true))
                    {
                        allDisconnected = false;
                    }
                }
            }

            if (allDisconnected)
                break;

            Thread.Sleep(200);
        }
    }

    /// <summary>
    /// 从网络路径中提取服务器路径。
    /// \\192.168.1.2\share folder → \\192.168.1.2
    /// </summary>
    internal static string? ExtractServerPath(string networkPath)
    {
        var match = Regex.Match(networkPath, @"^\\\\[^\\]+");
        return match.Success ? match.Value : null;
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
    private const int ERROR_EXTENDED_ERROR = 1208; // WNet 扩展错误，connectResult 包含实际错误码
}