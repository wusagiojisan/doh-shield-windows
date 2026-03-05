using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DohShield.Core.Interop;

/// <summary>
/// WinDivert 2.2.x P/Invoke 宣告
/// 官方文件：https://reqrypt.org/windivert-doc.html
/// </summary>
public static class WinDivert
{
    private const string DllName = "WinDivert";

    public static readonly IntPtr InvalidHandle = new(-1);

    // ──────── Layer ────────
    public enum Layer : int
    {
        Network = 0,
        NetworkForward = 1,
        Flow = 2,
        Socket = 3,
        Reflect = 4
    }

    // ──────── WinDivertHelperCalcChecksums flags ────────
    [Flags]
    public enum ChecksumFlags : ulong
    {
        None = 0,
        NoIpChecksum = 1,
        NoIcmpChecksum = 2,
        NoIcmpV6Checksum = 4,
        NoTcpChecksum = 8,
        NoUdpChecksum = 16
    }

    // ──────── Core API ────────

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr WinDivertOpen(
        string filter,
        Layer layer,
        short priority,
        ulong flags
    );

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool WinDivertRecv(
        IntPtr handle,
        [Out] byte[] pPacket,
        uint packetLen,
        out uint pRecvLen,
        ref WinDivertAddress pAddr
    );

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool WinDivertSend(
        IntPtr handle,
        byte[] pPacket,
        uint packetLen,
        out uint pSendLen,
        ref WinDivertAddress pAddr
    );

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool WinDivertClose(IntPtr handle);

    // ──────── Helper API ────────

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool WinDivertHelperCalcChecksums(
        byte[] pPacket,
        uint packetLen,
        ref WinDivertAddress pAddr,
        ulong flags
    );

    // ──────── 啟動時驗證 struct 大小 ────────

    /// <summary>
    /// 啟動時必須呼叫此方法，確認 WinDivertAddress struct 大小為 80 bytes
    /// </summary>
    public static void AssertStructSize()
    {
        int size = Marshal.SizeOf<WinDivertAddress>();
        Debug.Assert(size == 80,
            $"WinDivertAddress 大小錯誤：應為 80 bytes，實際為 {size} bytes");
    }
}
