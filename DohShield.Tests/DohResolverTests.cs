using DohShield.Core.Dns;
using DohShield.Core.Network;
using Xunit;

namespace DohShield.Tests;

/// <summary>
/// Fake IDohClient - 測試用，可預設每個 URL 的回傳結果
/// </summary>
file sealed class FakeDohClient : IDohClient
{
    private readonly Dictionary<string, Func<byte[], byte[]>> _handlers = new();
    private readonly Dictionary<string, Exception?> _errors = new();

    public void SetSuccess(string url, byte[] response)
        => _handlers[url] = _ => response;

    public void SetError(string url, Exception? ex = null)
        => _errors[url] = ex ?? new HttpRequestException("fake error");

    public Task<byte[]> QueryAsync(string serverUrl, byte[] queryBytes, CancellationToken ct = default)
    {
        if (_errors.TryGetValue(serverUrl, out var ex))
            return Task.FromException<byte[]>(ex!);
        if (_handlers.TryGetValue(serverUrl, out var fn))
            return Task.FromResult(fn(queryBytes));
        return Task.FromException<byte[]>(new HttpRequestException($"No handler for {serverUrl}"));
    }
}

public class DohResolverTests
{
    private const string PrimaryUrl = "https://primary.test/dns-query";
    private const string FallbackUrl = "https://fallback.test/dns-query";

    private static byte[] BuildQuery(string domain = "example.com", int txId = 0x0001)
    {
        var labels = domain.Split('.');
        int nameLen = labels.Sum(l => 1 + l.Length) + 1;
        var packet = new byte[12 + nameLen + 4];
        packet[0] = (byte)(txId >> 8);
        packet[1] = (byte)(txId & 0xFF);
        packet[2] = 0x01;
        packet[4] = 0x00; packet[5] = 0x01;
        int offset = 12;
        foreach (var label in labels)
        {
            packet[offset++] = (byte)label.Length;
            foreach (char c in label) packet[offset++] = (byte)c;
        }
        packet[offset++] = 0;
        packet[offset++] = 0; packet[offset++] = 1; // QTYPE=A
        packet[offset++] = 0; packet[offset] = 1;   // QCLASS=IN
        return packet;
    }

    /// <summary>建立一個帶 TTL=300 的假 DNS response（ANCOUNT=1）</summary>
    private static byte[] FakeResponse(int txId = 0x0001, int ttlSeconds = 300)
    {
        var resp = new byte[28];
        resp[0] = (byte)(txId >> 8); resp[1] = (byte)(txId & 0xFF);
        resp[2] = 0x81; resp[3] = 0x80; // QR=1, RD=1, RA=1
        resp[4] = 0; resp[5] = 1;       // QDCOUNT=1
        resp[6] = 0; resp[7] = 1;       // ANCOUNT=1
        // QNAME: [7]example[3]com[0]
        resp[12] = 7; "example".ToCharArray().Select(c => (byte)c).ToArray().CopyTo(resp, 13);
        resp[20] = 3; "com".ToCharArray().Select(c => (byte)c).ToArray().CopyTo(resp, 21);
        resp[24] = 0;
        resp[25] = 0; resp[26] = 1;  // QTYPE=A
        resp[27] = 0;                // (truncated for simplicity)
        return resp;
    }

    // ──────── 快取命中 ────────

    [Fact]
    public async Task Resolve_CacheHit_DoesNotCallServer()
    {
        var fake = new FakeDohClient();
        var cache = new DnsCache();
        var cachedBytes = FakeResponse(0xABCD);
        cache.Put("example.com", 1, cachedBytes, 300);

        var resolver = new DohResolver(fake, cache, PrimaryUrl, FallbackUrl);
        var rawQuery = BuildQuery("example.com", txId: 0x0001);
        var query = DnsPacketParser.ParseQuery(rawQuery)!;

        var result = await resolver.ResolveAsync(query, rawQuery);

        // transaction ID 應替換為本次 query 的 ID (0x0001)
        Assert.Equal(0x00, result[0]);
        Assert.Equal(0x01, result[1]);
    }

    // ──────── 主要伺服器成功 ────────

    [Fact]
    public async Task Resolve_PrimarySuccess_ReturnsPrimaryResponse()
    {
        var fake = new FakeDohClient();
        var expected = FakeResponse();
        fake.SetSuccess(PrimaryUrl, expected);

        var resolver = new DohResolver(fake, new DnsCache(), PrimaryUrl, FallbackUrl);
        var rawQuery = BuildQuery();
        var query = DnsPacketParser.ParseQuery(rawQuery)!;

        var result = await resolver.ResolveAsync(query, rawQuery);
        Assert.Equal(expected, result);
    }

    // ──────── 主要伺服器失敗 → Fallback ────────

    [Fact]
    public async Task Resolve_PrimaryFails_FallsBackToFallback()
    {
        var fake = new FakeDohClient();
        fake.SetError(PrimaryUrl);
        var fallbackResponse = FakeResponse();
        fake.SetSuccess(FallbackUrl, fallbackResponse);

        // 設定 failureThreshold=5 讓 circuit breaker 不要太快 open
        var cb = new CircuitBreaker(failureThreshold: 10);
        var resolver = new DohResolver(fake, new DnsCache(), PrimaryUrl, FallbackUrl,
            circuitBreaker: cb);

        var rawQuery = BuildQuery();
        var query = DnsPacketParser.ParseQuery(rawQuery)!;
        var result = await resolver.ResolveAsync(query, rawQuery);

        Assert.Equal(fallbackResponse, result);
    }

    // ──────── 全部失敗 → SERVFAIL ────────

    [Fact]
    public async Task Resolve_AllFail_ReturnsServfail()
    {
        var fake = new FakeDohClient();
        fake.SetError(PrimaryUrl);
        fake.SetError(FallbackUrl);

        var cb = new CircuitBreaker(failureThreshold: 100); // 防止 circuit breaker 影響
        var resolver = new DohResolver(fake, new DnsCache(), PrimaryUrl, FallbackUrl,
            circuitBreaker: cb);

        var rawQuery = BuildQuery("fail.test");
        var query = DnsPacketParser.ParseQuery(rawQuery)!;
        var result = await resolver.ResolveAsync(query, rawQuery);

        // SERVFAIL: QR=1, RCODE=2
        Assert.True((result[2] & 0x80) != 0);
        Assert.Equal(2, result[3] & 0x0F);
    }

    // ──────── Circuit Breaker 整合 ────────

    [Fact]
    public async Task Resolve_CircuitOpen_SkipsPrimaryGoesToFallback()
    {
        var fake = new FakeDohClient();
        // primary 沒有 handler → 會 exception
        var fallbackResponse = FakeResponse();
        fake.SetSuccess(FallbackUrl, fallbackResponse);

        // 已 Open 的 circuit breaker
        var cb = new CircuitBreaker(failureThreshold: 1, recoveryTimeoutMs: 60_000);
        cb.RecordFailure(); // → Open
        Assert.Equal(CircuitBreaker.State.Open, cb.CurrentState);

        var resolver = new DohResolver(fake, new DnsCache(), PrimaryUrl, FallbackUrl,
            circuitBreaker: cb);

        var rawQuery = BuildQuery();
        var query = DnsPacketParser.ParseQuery(rawQuery)!;
        var result = await resolver.ResolveAsync(query, rawQuery);

        Assert.Equal(fallbackResponse, result);
    }

    // ──────── 查詢 log 回呼 ────────

    [Fact]
    public async Task Resolve_LogsQueryOnSuccess()
    {
        var fake = new FakeDohClient();
        fake.SetSuccess(PrimaryUrl, FakeResponse());

        string? loggedDomain = null;
        bool? loggedCached = null;

        var resolver = new DohResolver(fake, new DnsCache(), PrimaryUrl, FallbackUrl,
            onQueryLogged: (domain, type, cached, ms, server) =>
            {
                loggedDomain = domain;
                loggedCached = cached;
            });

        var rawQuery = BuildQuery("example.com");
        var query = DnsPacketParser.ParseQuery(rawQuery)!;
        await resolver.ResolveAsync(query, rawQuery);

        Assert.Equal("example.com", loggedDomain);
        Assert.False(loggedCached);
    }
}
