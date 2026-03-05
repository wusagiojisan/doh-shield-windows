using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using DohShield.Core.Dns;
using DohShield.Core.Interop;
using DohShield.Core.Network;
using DohShield.Data;

namespace DohShield.Engine;

/// <summary>
/// WinDivert 攔截主迴圈
/// 對應 Android VpnEngine.kt，但攔截機制從 TUN fd 換成 WinDivert
///
/// 封包管線：
///   WinDivertOpen("udp.DstPort == 53")
///   → WinDivertRecv()  [blocking，移除封包]
///   → Task.Run: ParseIp → ExtractUdpPayload → ParseDnsQuery
///   → DohResolver.ResolveAsync()
///   → BuildResponsePacket（swap src/dst IP+Port，替換 DNS payload）
///   → WinDivertHelperCalcChecksums()
///   → WinDivertSend(addr as INBOUND)
/// </summary>
public sealed class WinDivertEngine
{
    private const string Filter = "udp.DstPort == 53";
    private const int MaxPacketSize = 65535;

    private readonly DnsLogRepository _repository;
    private readonly AppSettings _settings;

    private IntPtr _handle = WinDivert.InvalidHandle;
    private CancellationTokenSource? _cts;
    private Task? _captureTask;

    private DohResolver? _resolver;
    private DnsCache? _cache;

    // ──────── 統計 ────────
    private long _totalQueries;
    private long _cachedQueries;
    private long _totalResponseTimeMs;
    private long _nonCachedCount;

    public long TotalQueries => Interlocked.Read(ref _totalQueries);
    public long CachedQueries => Interlocked.Read(ref _cachedQueries);
    public double AvgResponseTimeMs
    {
        get
        {
            long cnt = Interlocked.Read(ref _nonCachedCount);
            return cnt == 0 ? 0 : (double)Interlocked.Read(ref _totalResponseTimeMs) / cnt;
        }
    }

    // ──────── 狀態與事件 ────────
    private EngineState _state = EngineState.Stopped;

    public EngineState State
    {
        get => _state;
        private set
        {
            _state = value;
            StateChanged?.Invoke(this, value);
        }
    }

    public event EventHandler<EngineState>? StateChanged;
    public event EventHandler<DnsQueryLog>? QueryLogged;
    public event EventHandler<string>? ErrorOccurred;

    public WinDivertEngine(DnsLogRepository repository, AppSettings settings)
    {
        _repository = repository;
        _settings = settings;
    }

    // ──────────────────────────────────────
    //  啟動 / 停止
    // ──────────────────────────────────────

    public async Task StartAsync()
    {
        if (State != EngineState.Stopped) return;

        State = EngineState.Starting;
        WinDivert.AssertStructSize();

        try
        {
            // ── Bootstrap map：讓 HttpClient 直連 IP，避免 DoH 請求被 WinDivert 攔截 ──
            var bootstrapMap = await BuildBootstrapMap(_settings);

            // 建立 DohResolver
            _cache = new DnsCache();
            var client = new DohClient(bootstrapMap);
            _resolver = new DohResolver(
                dohClient: client,
                dnsCache: _cache,
                primaryServerUrl: _settings.ActiveServerUrl,
                fallbackServerUrl: _settings.FallbackServerUrl,
                onQueryLogged: OnQueryLogged
            );

            // 開啟 WinDivert handle
            _handle = WinDivert.WinDivertOpen(Filter, WinDivert.Layer.Network, 0, 0);
            if (_handle == WinDivert.InvalidHandle)
            {
                int err = Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"WinDivertOpen 失敗，Win32 error: {err}");
            }

            // 重置統計
            Interlocked.Exchange(ref _totalQueries, 0);
            Interlocked.Exchange(ref _cachedQueries, 0);
            Interlocked.Exchange(ref _totalResponseTimeMs, 0);
            Interlocked.Exchange(ref _nonCachedCount, 0);

            _cts = new CancellationTokenSource();
            _captureTask = Task.Run(() => CaptureLoop(_cts.Token));

            State = EngineState.Running;
        }
        catch (Exception ex)
        {
            CloseHandle();
            State = EngineState.Error;
            ErrorOccurred?.Invoke(this, ex.Message);
        }

        await Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (State == EngineState.Stopped) return;

        _cts?.Cancel();
        CloseHandle(); // 關閉 handle 使 WinDivertRecv 立即返回

        if (_captureTask != null)
        {
            try { await _captureTask.WaitAsync(TimeSpan.FromSeconds(3)); }
            catch { /* 忽略取消/逾時 */ }
        }

        _cts?.Dispose();
        _cts = null;
        _captureTask = null;
        _cache?.Clear();

        State = EngineState.Stopped;
    }

    // ──────────────────────────────────────
    //  攔截主迴圈
    // ──────────────────────────────────────

    private void CaptureLoop(CancellationToken ct)
    {
        var buffer = new byte[MaxPacketSize];

        while (!ct.IsCancellationRequested)
        {
            var addr = WinDivertAddress.Create();

            bool ok = WinDivert.WinDivertRecv(
                _handle,
                buffer,
                (uint)buffer.Length,
                out uint recvLen,
                ref addr
            );

            if (!ok || ct.IsCancellationRequested) break;
            if (recvLen == 0) continue;

            // 複製封包（buffer 會被下一次 Recv 覆寫）
            var packet = buffer[..(int)recvLen];

            // 非同步處理，不阻塞 Recv 迴圈
            _ = Task.Run(() => ProcessPacketAsync(packet, addr, ct), ct);
        }
    }

    private async Task ProcessPacketAsync(byte[] packet, WinDivertAddress addr, CancellationToken ct)
    {
        try
        {
            if (!TryParseIpUdpDns(packet, out int ipHdrLen, out var dnsQuery, out byte[]? rawDns))
                return;

            if (dnsQuery == null || rawDns == null) return;

            var resolver = _resolver;
            if (resolver == null) return;

            byte[] dnsResponse = await resolver.ResolveAsync(dnsQuery, rawDns, ct);

            byte[]? responsePacket = BuildResponsePacket(packet, ipHdrLen, dnsResponse, addr);
            if (responsePacket == null) return;

            // 計算 checksum
            var replyAddr = addr;
            replyAddr.Outbound = false; // INBOUND 方向注入

            WinDivert.WinDivertHelperCalcChecksums(
                responsePacket,
                (uint)responsePacket.Length,
                ref replyAddr,
                (ulong)WinDivert.ChecksumFlags.None
            );

            WinDivert.WinDivertSend(
                _handle,
                responsePacket,
                (uint)responsePacket.Length,
                out _,
                ref replyAddr
            );
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"封包處理錯誤: {ex.Message}");
        }
    }

    // ──────────────────────────────────────
    //  封包解析
    // ──────────────────────────────────────

    /// <summary>
    /// 解析 IP + UDP header，取出 DNS payload 並解析 DNS query
    /// </summary>
    private static bool TryParseIpUdpDns(
        byte[] packet,
        out int ipHdrLen,
        out DnsPacketParser.DnsQuery? dnsQuery,
        out byte[]? rawDns)
    {
        ipHdrLen = 0;
        dnsQuery = null;
        rawDns = null;

        if (packet.Length < 28) return false; // 最小 IP(20) + UDP(8)

        int ipVersion = (packet[0] >> 4);

        if (ipVersion == 4)
        {
            ipHdrLen = (packet[0] & 0x0F) * 4;
            if (ipHdrLen < 20 || packet.Length < ipHdrLen + 8) return false;
        }
        else if (ipVersion == 6)
        {
            ipHdrLen = 40; // IPv6 fixed header
            if (packet.Length < ipHdrLen + 8) return false;
        }
        else
        {
            return false;
        }

        // UDP dst port（offset ipHdrLen+2, big-endian）
        int dstPort = (packet[ipHdrLen + 2] << 8) | packet[ipHdrLen + 3];
        if (dstPort != 53) return false;

        int udpPayloadOffset = ipHdrLen + 8;
        if (packet.Length <= udpPayloadOffset) return false;

        rawDns = packet[udpPayloadOffset..];
        dnsQuery = DnsPacketParser.ParseQuery(rawDns);
        return dnsQuery != null;
    }

    // ──────────────────────────────────────
    //  回應封包建構
    // ──────────────────────────────────────

    /// <summary>
    /// 建構 DNS response 封包：
    /// 1. Swap src/dst IP
    /// 2. Swap UDP src/dst port
    /// 3. 替換 DNS payload
    /// 4. 更新 UDP length 與 IP total length
    /// 5. 清零 IP/UDP checksum（由 WinDivertHelperCalcChecksums 重算）
    /// </summary>
    private static byte[]? BuildResponsePacket(byte[] origPacket, int ipHdrLen, byte[] dnsResponse, WinDivertAddress addr)
    {
        bool isIPv6 = addr.IPv6;
        int newTotalLen = ipHdrLen + 8 + dnsResponse.Length;

        if (origPacket.Length < ipHdrLen + 8) return null;

        var resp = new byte[newTotalLen];

        // 複製 IP header
        Array.Copy(origPacket, 0, resp, 0, ipHdrLen);

        if (!isIPv6)
        {
            // ── IPv4：swap src (offset 12) ↔ dst (offset 16) ──
            Array.Copy(origPacket, 16, resp, 12, 4); // dst → src
            Array.Copy(origPacket, 12, resp, 16, 4); // src → dst

            // 更新 IP total length（offset 2, 2 bytes, big-endian）
            resp[2] = (byte)(newTotalLen >> 8);
            resp[3] = (byte)(newTotalLen & 0xFF);

            // 清零 IP checksum（offset 10-11）
            resp[10] = 0;
            resp[11] = 0;
        }
        else
        {
            // ── IPv6：swap src (offset 8, 16 bytes) ↔ dst (offset 24, 16 bytes) ──
            Array.Copy(origPacket, 24, resp, 8, 16);  // dst → src
            Array.Copy(origPacket, 8, resp, 24, 16);  // src → dst

            // 更新 IPv6 Payload Length（offset 4, 2 bytes, big-endian）
            int payloadLen = 8 + dnsResponse.Length;
            resp[4] = (byte)(payloadLen >> 8);
            resp[5] = (byte)(payloadLen & 0xFF);
        }

        // ── UDP header（offset ipHdrLen）──
        // Swap UDP src port (offset+0) ↔ dst port (offset+2)
        resp[ipHdrLen + 0] = origPacket[ipHdrLen + 2]; // dst port → src port（原 53）
        resp[ipHdrLen + 1] = origPacket[ipHdrLen + 3];
        resp[ipHdrLen + 2] = origPacket[ipHdrLen + 0]; // src port → dst port
        resp[ipHdrLen + 3] = origPacket[ipHdrLen + 1];

        // UDP length（offset+4, 2 bytes, big-endian）= 8 + dnsPayload
        int udpLen = 8 + dnsResponse.Length;
        resp[ipHdrLen + 4] = (byte)(udpLen >> 8);
        resp[ipHdrLen + 5] = (byte)(udpLen & 0xFF);

        // 清零 UDP checksum（offset+6-7）
        resp[ipHdrLen + 6] = 0;
        resp[ipHdrLen + 7] = 0;

        // ── DNS payload ──
        Array.Copy(dnsResponse, 0, resp, ipHdrLen + 8, dnsResponse.Length);

        return resp;
    }

    // ──────────────────────────────────────
    //  查詢紀錄回呼
    // ──────────────────────────────────────

    private void OnQueryLogged(string domain, int type, bool cached, long responseTimeMs, string resolvedBy)
    {
        Interlocked.Increment(ref _totalQueries);
        if (cached)
        {
            Interlocked.Increment(ref _cachedQueries);
        }
        else if (responseTimeMs > 0)
        {
            Interlocked.Add(ref _totalResponseTimeMs, responseTimeMs);
            Interlocked.Increment(ref _nonCachedCount);
        }

        // 寫入 DB（fire and forget）
        _ = _repository.LogQueryAsync(domain, type, cached, responseTimeMs, resolvedBy)
            .ContinueWith(t =>
            {
                if (t.IsFaulted)
                    ErrorOccurred?.Invoke(this, $"寫入紀錄失敗: {t.Exception?.InnerException?.Message}");
            });

        // 通知 UI
        var log = new DnsQueryLog
        {
            Domain = domain,
            Type = type,
            Cached = cached,
            ResponseTimeMs = responseTimeMs,
            ResolvedBy = resolvedBy,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        QueryLogged?.Invoke(this, log);
    }

    // ──────────────────────────────────────
    //  輔助
    // ──────────────────────────────────────

    /// <summary>
    /// 建立 hostname → bootstrap IP 的對應表。
    ///
    /// 目的：讓 DohClient 的 HttpClient 直連 bootstrap IP，跳過 DNS 解析，
    /// 避免 DoH 請求本身也被 WinDivert 攔截造成遞迴鎖死。
    ///
    /// 流程：
    ///   1. 先把所有已知 Preset 的 bootstrap IPs 加入 map（即時可用）
    ///   2. 若使用自訂伺服器，在 WinDivert 啟動前做一次 DNS 解析，把結果存入 map
    /// </summary>
    private static async Task<IReadOnlyDictionary<string, string>> BuildBootstrapMap(AppSettings settings)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 加入所有 Preset 伺服器的 bootstrap IP
        foreach (var preset in DohServers.Presets)
        {
            if (preset.BootstrapIps == null) continue;
            try
            {
                string host = new Uri(preset.Url).Host;
                if (!map.ContainsKey(host))
                    map[host] = preset.BootstrapIps[0];
            }
            catch { /* ignore */ }
        }

        // 自訂 URL：WinDivert 啟動前先解析 hostname（此時 DNS 還沒被攔截）
        if (settings.ServerId == "custom" && !string.IsNullOrWhiteSpace(settings.CustomServerUrl))
        {
            try
            {
                var uri = new Uri(settings.CustomServerUrl);
                if (!map.ContainsKey(uri.Host))
                {
                    var addrs = await Dns.GetHostAddressesAsync(uri.Host);
                    var ip = Array.Find(addrs, a => a.AddressFamily == AddressFamily.InterNetwork)
                             ?? addrs.FirstOrDefault();
                    if (ip != null)
                        map[uri.Host] = ip.ToString();
                }
            }
            catch { /* 解析失敗：DohClient 會 fallback 到系統 DNS（可能失敗） */ }
        }

        return map;
    }

    private void CloseHandle()
    {
        if (_handle != WinDivert.InvalidHandle)
        {
            WinDivert.WinDivertClose(_handle);
            _handle = WinDivert.InvalidHandle;
        }
    }
}
