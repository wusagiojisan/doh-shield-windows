using System.Text;

namespace DohShield.Core.Dns;

/// <summary>
/// DNS wire format 解析（手動實作，不依賴外部 DNS 函式庫）
/// 移植自 Android DnsPacketParser.kt，Android 用 dnsjava；Windows 版自行解析
/// </summary>
public static class DnsPacketParser
{
    public sealed record DnsQuery(
        int TransactionId,
        string Domain,
        int Type,       // A=1, AAAA=28 等
        byte[] RawQuery
    );

    /// <summary>
    /// 解析 DNS query bytes
    /// </summary>
    /// <returns>null 表示不是有效的 DNS query</returns>
    public static DnsQuery? ParseQuery(byte[] data)
    {
        if (data.Length < 12) return null;

        // DNS Header
        int txId = (data[0] << 8) | data[1];
        int flags = (data[2] << 8) | data[3];

        // QR bit（bit 15）= 0 表示 query，= 1 表示 response
        bool isQuery = (flags & 0x8000) == 0;
        if (!isQuery) return null;

        int qdCount = (data[4] << 8) | data[5];
        if (qdCount == 0) return null;

        // 解析第一個 Question
        int offset = 12;
        string? domain = ParseDnsName(data, ref offset);
        if (domain == null || offset + 4 > data.Length) return null;

        int qtype = (data[offset] << 8) | data[offset + 1];
        // offset += 4; // QTYPE + QCLASS（不需要繼續解析）

        return new DnsQuery(txId, domain, qtype, data);
    }

    /// <summary>
    /// 複製 response 並替換 Transaction ID（快取命中時使用）
    /// </summary>
    public static byte[] ReplaceTransactionId(byte[] responseBytes, int newId)
    {
        if (responseBytes.Length < 2) return responseBytes;
        var copy = (byte[])responseBytes.Clone();
        copy[0] = (byte)((newId >> 8) & 0xFF);
        copy[1] = (byte)(newId & 0xFF);
        return copy;
    }

    /// <summary>
    /// 從 DNS response 取出所有 Answer RR 中最小的 TTL（秒）
    /// </summary>
    public static long ExtractMinTtl(byte[] data)
    {
        if (data.Length < 12) return 60;

        try
        {
            int qdCount = (data[4] << 8) | data[5];
            int anCount = (data[6] << 8) | data[7];

            int offset = 12;

            // 略過 Question section
            for (int i = 0; i < qdCount; i++)
            {
                SkipDnsName(data, ref offset);
                offset += 4; // QTYPE + QCLASS
                if (offset > data.Length) return 60;
            }

            long minTtl = 60;
            bool found = false;

            // 解析 Answer section
            for (int i = 0; i < anCount; i++)
            {
                SkipDnsName(data, ref offset);
                if (offset + 10 > data.Length) break;

                // TYPE + CLASS（各 2 bytes）
                offset += 4;

                // TTL（4 bytes，big-endian，有號數）
                int ttlRaw = (data[offset] << 24) | (data[offset + 1] << 16)
                           | (data[offset + 2] << 8) | data[offset + 3];
                long ttl = Math.Max(1L, (long)(ttlRaw < 0 ? 0 : ttlRaw));
                offset += 4;

                // RDLENGTH（2 bytes）
                if (offset + 2 > data.Length) break;
                int rdLen = (data[offset] << 8) | data[offset + 1];
                offset += 2 + rdLen;

                if (!found || ttl < minTtl)
                {
                    minTtl = ttl;
                    found = true;
                }
            }

            return found ? minTtl : 60L;
        }
        catch
        {
            return 60L;
        }
    }

    /// <summary>
    /// 根據原始 query bytes 建構 SERVFAIL response
    /// </summary>
    public static byte[]? BuildServfailResponse(byte[] queryBytes)
    {
        if (queryBytes.Length < 12) return null;
        var response = (byte[])queryBytes.Clone();

        // Byte[2]: QR=1（response），TC=0
        response[2] = (byte)((response[2] | 0x80) & 0xFB);
        // Byte[3]: RCODE=2（SERVFAIL），清 RA/Z/AD/CD
        response[3] = (byte)((response[3] & 0xF0) | 0x02);
        // 清除 Answer/Authority/Additional counts
        response[6] = 0; response[7] = 0;
        response[8] = 0; response[9] = 0;
        response[10] = 0; response[11] = 0;

        return response;
    }

    // ──────────────────────────────────────────────────
    // 內部輔助方法
    // ──────────────────────────────────────────────────

    /// <summary>解析 DNS name，支援壓縮指標</summary>
    private static string? ParseDnsName(byte[] data, ref int offset)
    {
        var labels = new List<string>();
        int? jumpBack = null;
        int safety = 0;

        while (offset < data.Length && safety++ < 128)
        {
            byte len = data[offset];

            if (len == 0)
            {
                offset++;
                break;
            }

            // 壓縮指標：高 2 bits = 11
            if ((len & 0xC0) == 0xC0)
            {
                if (offset + 1 >= data.Length) return null;
                int ptr = ((len & 0x3F) << 8) | data[offset + 1];
                jumpBack ??= offset + 2;
                offset = ptr;
                continue;
            }

            offset++;
            if (offset + len > data.Length) return null;
            labels.Add(Encoding.ASCII.GetString(data, offset, len));
            offset += len;
        }

        if (jumpBack.HasValue) offset = jumpBack.Value;
        return string.Join(".", labels);
    }

    /// <summary>略過一個 DNS name（不解析內容）</summary>
    private static void SkipDnsName(byte[] data, ref int offset)
    {
        int safety = 0;
        while (offset < data.Length && safety++ < 128)
        {
            byte len = data[offset];
            if (len == 0) { offset++; return; }
            if ((len & 0xC0) == 0xC0) { offset += 2; return; }
            offset += 1 + len;
        }
    }
}
