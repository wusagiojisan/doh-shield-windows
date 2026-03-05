namespace DohShield.Core.Dns;

/// <summary>
/// DNS LRU Cache + TTL 支援
/// key = "domain:type"（例如 "google.com:1"）
/// 移植自 Android DnsCache.kt，LinkedHashMap LRU → LruDictionary
/// </summary>
public sealed class DnsCache
{
    private readonly int _maxSize;
    private readonly Dictionary<string, LinkedListNode<(string Key, DnsCacheEntry Entry)>> _map;
    private readonly LinkedList<(string Key, DnsCacheEntry Entry)> _list;
    private readonly object _lock = new();

    public DnsCache(int maxSize = Constants.DnsCacheMaxSize)
    {
        _maxSize = maxSize;
        _map = new Dictionary<string, LinkedListNode<(string, DnsCacheEntry)>>(maxSize);
        _list = new LinkedList<(string, DnsCacheEntry)>();
    }

    private static string MakeKey(string domain, int type) => $"{domain}:{type}";

    /// <summary>查詢快取，未命中或已過期回傳 null</summary>
    public byte[]? Get(string domain, int type)
    {
        lock (_lock)
        {
            string key = MakeKey(domain, type);
            if (!_map.TryGetValue(key, out var node))
                return null;

            if (node.Value.Entry.IsExpired())
            {
                _list.Remove(node);
                _map.Remove(key);
                return null;
            }

            // 移到最新（LRU 更新）
            _list.Remove(node);
            _list.AddFirst(node);
            return node.Value.Entry.ResponseBytes;
        }
    }

    /// <summary>寫入快取</summary>
    public void Put(string domain, int type, byte[] responseBytes, long ttlSeconds)
    {
        lock (_lock)
        {
            string key = MakeKey(domain, type);
            var expiresAt = DateTimeOffset.UtcNow.AddSeconds(ttlSeconds);
            var entry = new DnsCacheEntry(responseBytes, expiresAt);

            if (_map.TryGetValue(key, out var existing))
            {
                _list.Remove(existing);
                _map.Remove(key);
            }

            var node = _list.AddFirst((key, entry));
            _map[key] = node;

            // 超過最大容量時移除最舊
            while (_list.Count > _maxSize)
            {
                var oldest = _list.Last!;
                _list.RemoveLast();
                _map.Remove(oldest.Value.Key);
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _list.Clear();
            _map.Clear();
        }
    }

    public int Size()
    {
        lock (_lock) return _list.Count;
    }
}
