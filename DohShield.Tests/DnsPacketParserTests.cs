using DohShield.Core.Dns;
using Xunit;

namespace DohShield.Tests;

public class DnsPacketParserTests
{
    // ──────── 建立測試用 DNS query bytes ────────

    /// <summary>
    /// 建立標準的 DNS query（A record for "example.com"）
    /// Transaction ID = 0x1234
    /// </summary>
    private static byte[] BuildDnsQuery(string domain, int txId = 0x1234, int qtype = 1)
    {
        var labels = domain.Split('.');
        int nameLen = labels.Sum(l => 1 + l.Length) + 1; // length prefix + bytes + terminating 0

        var packet = new byte[12 + nameLen + 4]; // header + qname + qtype + qclass
        // Header
        packet[0] = (byte)(txId >> 8);
        packet[1] = (byte)(txId & 0xFF);
        packet[2] = 0x01; // RD=1
        packet[3] = 0x00;
        packet[4] = 0x00; packet[5] = 0x01; // QDCOUNT=1
        packet[6] = 0x00; packet[7] = 0x00; // ANCOUNT=0
        packet[8] = 0x00; packet[9] = 0x00; // NSCOUNT=0
        packet[10] = 0x00; packet[11] = 0x00; // ARCOUNT=0

        int offset = 12;
        foreach (var label in labels)
        {
            packet[offset++] = (byte)label.Length;
            foreach (char c in label)
                packet[offset++] = (byte)c;
        }
        packet[offset++] = 0x00; // 結尾
        packet[offset++] = (byte)(qtype >> 8);
        packet[offset++] = (byte)(qtype & 0xFF);
        packet[offset++] = 0x00; packet[offset] = 0x01; // QCLASS=IN

        return packet;
    }

    // ──────── ParseQuery ────────

    [Fact]
    public void ParseQuery_ValidAQuery_ReturnsDnsQuery()
    {
        var data = BuildDnsQuery("example.com", txId: 0x1234, qtype: 1);
        var result = DnsPacketParser.ParseQuery(data);

        Assert.NotNull(result);
        Assert.Equal(0x1234, result.TransactionId);
        Assert.Equal("example.com", result.Domain);
        Assert.Equal(1, result.Type);
    }

    [Fact]
    public void ParseQuery_AaaaRecord_ReturnsType28()
    {
        var data = BuildDnsQuery("google.com", txId: 0xABCD, qtype: 28);
        var result = DnsPacketParser.ParseQuery(data);

        Assert.NotNull(result);
        Assert.Equal(28, result.Type);
        Assert.Equal("google.com", result.Domain);
    }

    [Fact]
    public void ParseQuery_TooShort_ReturnsNull()
    {
        var result = DnsPacketParser.ParseQuery(new byte[8]);
        Assert.Null(result);
    }

    [Fact]
    public void ParseQuery_ResponsePacket_ReturnsNull()
    {
        var data = BuildDnsQuery("example.com");
        // 設 QR bit = 1（response）
        data[2] |= 0x80;
        var result = DnsPacketParser.ParseQuery(data);
        Assert.Null(result);
    }

    [Fact]
    public void ParseQuery_SubdomainDot_ParsedCorrectly()
    {
        var data = BuildDnsQuery("www.example.com");
        var result = DnsPacketParser.ParseQuery(data);

        Assert.NotNull(result);
        Assert.Equal("www.example.com", result.Domain);
    }

    // ──────── ReplaceTransactionId ────────

    [Fact]
    public void ReplaceTransactionId_ReplacesCorrectly()
    {
        var data = new byte[] { 0x12, 0x34, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        var result = DnsPacketParser.ReplaceTransactionId(data, 0xABCD);

        Assert.Equal(0xAB, result[0]);
        Assert.Equal(0xCD, result[1]);
        // 原始未修改
        Assert.Equal(0x12, data[0]);
    }

    [Fact]
    public void ReplaceTransactionId_TooShort_ReturnsOriginal()
    {
        var data = new byte[] { 0x01 };
        var result = DnsPacketParser.ReplaceTransactionId(data, 0x1234);
        Assert.Equal(data, result);
    }

    // ──────── BuildServfailResponse ────────

    [Fact]
    public void BuildServfailResponse_SetsQrAndRcode()
    {
        var query = BuildDnsQuery("fail.test", txId: 0x0001);
        var servfail = DnsPacketParser.BuildServfailResponse(query);

        Assert.NotNull(servfail);
        // QR=1（bit 7 of byte[2]）
        Assert.True((servfail[2] & 0x80) != 0, "QR should be 1 (response)");
        // RCODE=2（SERVFAIL, lower 4 bits of byte[3]）
        Assert.Equal(2, servfail[3] & 0x0F);
        // Transaction ID preserved
        Assert.Equal(0x00, servfail[0]);
        Assert.Equal(0x01, servfail[1]);
        // Answer count = 0
        Assert.Equal(0, servfail[6]);
        Assert.Equal(0, servfail[7]);
    }

    [Fact]
    public void BuildServfailResponse_TooShort_ReturnsNull()
    {
        var result = DnsPacketParser.BuildServfailResponse(new byte[8]);
        Assert.Null(result);
    }

    // ──────── ExtractMinTtl ────────

    [Fact]
    public void ExtractMinTtl_NoAnswers_Returns60()
    {
        // 建立一個只有 header 的假 response（ANCOUNT=0）
        var data = new byte[12];
        data[2] = 0x80; // QR=1
        long ttl = DnsPacketParser.ExtractMinTtl(data);
        Assert.Equal(60, ttl);
    }
}
