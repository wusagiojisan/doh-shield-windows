using System.Runtime.InteropServices;

namespace DohShield.Core.Interop;

/// <summary>
/// WINDIVERT_ADDRESS struct — 必須剛好 80 bytes
///
/// C struct layout:
///   INT64  Timestamp;      // 8 bytes, offset 0
///   UINT32 Flags;          // 4 bytes, offset 8  (bit-fields: Layer:8, Event:8, Sniffed:1, Outbound:1, Loopback:1, Impostor:1, IPv6:1, ...)
///   UINT32 Reserved2;      // 4 bytes, offset 12
///   UINT8  Union[64];      // 64 bytes, offset 16  (WINDIVERT_DATA_NETWORK 等)
/// Total: 80 bytes
///
/// Flags bit-fields（從 LSB 起）:
///   bits  0-7   Layer
///   bits  8-15  Event
///   bit   16    Sniffed
///   bit   17    Outbound
///   bit   18    Loopback
///   bit   19    Impostor
///   bit   20    IPv6
///   bit   21    IPChecksum (rx only)
///   bit   22    TCPChecksum (rx only)
///   bit   23    UDPChecksum (rx only)
///   bits  24-31 Reserved1
///
/// Union（WINDIVERT_DATA_NETWORK，前 8 bytes）:
///   UINT32 IfIdx;      // offset 0
///   UINT32 SubIfIdx;   // offset 4
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct WinDivertAddress
{
    public long Timestamp;   // 8 bytes

    /// <summary>packed bit-fields（Layer、Event、Sniffed、Outbound、Loopback、Impostor、IPv6...）</summary>
    public uint Flags;       // 4 bytes

    public uint Reserved2;   // 4 bytes

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
    public byte[] Union;     // 64 bytes（marshaling size）

    // ──────── Flags 存取屬性 ────────

    public bool Outbound
    {
        get => (Flags & (1u << 17)) != 0;
        set { if (value) Flags |= 1u << 17; else Flags &= ~(1u << 17); }
    }

    public bool IPv6
    {
        get => (Flags & (1u << 20)) != 0;
    }

    // ──────── Union (WINDIVERT_DATA_NETWORK) 存取 ────────

    public uint IfIdx
    {
        get => Union == null ? 0u : BitConverter.ToUInt32(Union, 0);
        set { EnsureUnion(); BitConverter.GetBytes(value).CopyTo(Union, 0); }
    }

    public uint SubIfIdx
    {
        get => Union == null ? 0u : BitConverter.ToUInt32(Union, 4);
        set { EnsureUnion(); BitConverter.GetBytes(value).CopyTo(Union, 4); }
    }

    private void EnsureUnion()
    {
        Union ??= new byte[64];
    }

    /// <summary>建立一個已初始化的空白 address</summary>
    public static WinDivertAddress Create() => new() { Union = new byte[64] };
}
