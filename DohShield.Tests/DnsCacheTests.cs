using DohShield.Core.Dns;
using Xunit;

namespace DohShield.Tests;

public class DnsCacheTests
{
    private static readonly byte[] SampleResponse = [0x00, 0x01, 0x80, 0x00, 0xFF];

    [Fact]
    public void Get_EmptyCache_ReturnsNull()
    {
        var cache = new DnsCache(10);
        Assert.Null(cache.Get("example.com", 1));
    }

    [Fact]
    public void PutThenGet_ReturnsData()
    {
        var cache = new DnsCache(10);
        cache.Put("example.com", 1, SampleResponse, 300);

        var result = cache.Get("example.com", 1);
        Assert.NotNull(result);
        Assert.Equal(SampleResponse, result);
    }

    [Fact]
    public void Get_ExpiredEntry_ReturnsNull()
    {
        var cache = new DnsCache(10);
        cache.Put("example.com", 1, SampleResponse, 0); // TTL=0 → 立即過期

        // 等一小段時間確保過期
        System.Threading.Thread.Sleep(10);
        Assert.Null(cache.Get("example.com", 1));
    }

    [Fact]
    public void Put_DifferentTypes_StoredSeparately()
    {
        var cache = new DnsCache(10);
        var responseA = new byte[] { 0xAA };
        var responseAaaa = new byte[] { 0xBB };

        cache.Put("example.com", 1, responseA, 300);
        cache.Put("example.com", 28, responseAaaa, 300);

        Assert.Equal(responseA, cache.Get("example.com", 1));
        Assert.Equal(responseAaaa, cache.Get("example.com", 28));
        Assert.Equal(2, cache.Size());
    }

    [Fact]
    public void LruEviction_WhenAtCapacity_RemovesOldest()
    {
        var cache = new DnsCache(maxSize: 2);
        cache.Put("a.com", 1, [0x01], 300);
        cache.Put("b.com", 1, [0x02], 300);
        cache.Put("c.com", 1, [0x03], 300); // 超過容量，a.com 應被移除

        Assert.Null(cache.Get("a.com", 1));
        Assert.NotNull(cache.Get("b.com", 1));
        Assert.NotNull(cache.Get("c.com", 1));
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        var cache = new DnsCache(10);
        cache.Put("a.com", 1, [0x01], 300);
        cache.Put("b.com", 1, [0x02], 300);
        cache.Clear();

        Assert.Equal(0, cache.Size());
        Assert.Null(cache.Get("a.com", 1));
    }

    [Fact]
    public void Size_ReturnsCorrectCount()
    {
        var cache = new DnsCache(10);
        Assert.Equal(0, cache.Size());

        cache.Put("a.com", 1, SampleResponse, 300);
        Assert.Equal(1, cache.Size());

        cache.Put("b.com", 1, SampleResponse, 300);
        Assert.Equal(2, cache.Size());
    }
}
