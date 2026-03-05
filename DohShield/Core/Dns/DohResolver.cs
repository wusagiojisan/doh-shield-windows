using DohShield.Core.Network;

namespace DohShield.Core.Dns;

/// <summary>
/// DNS 解析核心邏輯：cache + circuit breaker + retry + fallback
/// 移植自 Android DohResolver.kt
///
/// 解析流程（cache miss）：
///   Circuit Closed：
///     1. 主要伺服器
///     2. 失敗 → 等 100ms → 重試主要伺服器
///     3. 仍失敗（連續 N 次後）→ Circuit Open → fallback
///   Circuit Open：
///     1. 直接走 fallback（不等待）
///     2. 30s 後 → HalfOpen → 試探主要伺服器 → 成功恢復 / 失敗繼續 Open
/// </summary>
public sealed class DohResolver
{
    private readonly IDohClient _dohClient;
    private readonly DnsCache _dnsCache;
    private readonly string _primaryServerUrl;
    private readonly string _fallbackServerUrl;
    private readonly Action<string, int, bool, long, string>? _onQueryLogged;

    internal readonly CircuitBreaker CircuitBreaker;

    public DohResolver(
        IDohClient dohClient,
        DnsCache dnsCache,
        string primaryServerUrl,
        string fallbackServerUrl,
        Action<string, int, bool, long, string>? onQueryLogged = null,
        CircuitBreaker? circuitBreaker = null)
    {
        _dohClient = dohClient;
        _dnsCache = dnsCache;
        _primaryServerUrl = primaryServerUrl;
        _fallbackServerUrl = fallbackServerUrl;
        _onQueryLogged = onQueryLogged;
        CircuitBreaker = circuitBreaker ?? new CircuitBreaker();
    }

    public async Task<byte[]> ResolveAsync(
        DnsPacketParser.DnsQuery query,
        byte[] rawQueryBytes,
        CancellationToken ct = default)
    {
        // 1. 查 cache
        var cached = _dnsCache.Get(query.Domain, query.Type);
        if (cached != null)
        {
            // 替換 Transaction ID 以符合本次查詢
            var response = DnsPacketParser.ReplaceTransactionId(cached, query.TransactionId);
            _onQueryLogged?.Invoke(query.Domain, query.Type, true, 0L, "");
            return response;
        }

        var startTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // 2. 主要伺服器（由 Circuit Breaker 決定）
        if (!CircuitBreaker.ShouldSkipPrimary())
        {
            var resp = await QueryServerAsync(_primaryServerUrl, rawQueryBytes, ct);
            if (resp != null)
            {
                CircuitBreaker.RecordSuccess();
                CacheAndLog(query, resp, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startTime, _primaryServerUrl);
                return resp;
            }
            CircuitBreaker.RecordFailure();

            // 重試一次（僅限 Closed 狀態；HalfOpen 只給一次機會）
            if (CircuitBreaker.ShouldRetry())
            {
                await Task.Delay(Constants.DohRetryDelayMs, ct);
                resp = await QueryServerAsync(_primaryServerUrl, rawQueryBytes, ct);
                if (resp != null)
                {
                    CircuitBreaker.RecordSuccess();
                    CacheAndLog(query, resp, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startTime, _primaryServerUrl);
                    return resp;
                }
                CircuitBreaker.RecordFailure();
            }
        }

        // 3. Fallback 伺服器
        var fallback = await QueryServerAsync(_fallbackServerUrl, rawQueryBytes, ct);
        if (fallback != null)
        {
            CacheAndLog(query, fallback, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startTime, _fallbackServerUrl);
            return fallback;
        }

        // 4. 全部失敗 → SERVFAIL
        return DnsPacketParser.BuildServfailResponse(rawQueryBytes) ?? rawQueryBytes;
    }

    private async Task<byte[]?> QueryServerAsync(string serverUrl, byte[] rawQueryBytes, CancellationToken ct)
    {
        try
        {
            return await _dohClient.QueryAsync(serverUrl, rawQueryBytes, ct);
        }
        catch
        {
            return null;
        }
    }

    private void CacheAndLog(DnsPacketParser.DnsQuery query, byte[] response, long responseTimeMs, string resolvedBy)
    {
        long ttl = DnsPacketParser.ExtractMinTtl(response);
        _dnsCache.Put(query.Domain, query.Type, response, ttl);
        _onQueryLogged?.Invoke(query.Domain, query.Type, false, responseTimeMs, resolvedBy);
    }
}
