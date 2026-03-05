using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;

namespace DohShield.Core.Network;

/// <summary>
/// RFC 8484 DNS-over-HTTPS 客戶端介面
/// </summary>
public interface IDohClient
{
    Task<byte[]> QueryAsync(string serverUrl, byte[] queryBytes, CancellationToken ct = default);
}

/// <summary>
/// RFC 8484 DNS-over-HTTPS 客戶端實作
/// 使用 POST application/dns-message
///
/// bootstrapMap：hostname → IP，讓 HttpClient 直連 bootstrap IP 而不走 DNS，
/// 避免 DoH 請求本身也被 WinDivert 攔截造成遞迴鎖死。
/// </summary>
public sealed class DohClient : IDohClient
{
    private static readonly MediaTypeHeaderValue DnsMessageContentType =
        new("application/dns-message");
    private static readonly MediaTypeWithQualityHeaderValue DnsMessageAccept =
        new("application/dns-message");

    private readonly HttpClient _http;

    /// <summary>測試用：注入 mock HttpClient</summary>
    public DohClient(HttpClient httpClient)
    {
        _http = httpClient;
    }

    /// <summary>
    /// 正式用：傳入 bootstrapMap（hostname → IP）讓 HttpClient 繞過 DNS 解析
    /// </summary>
    public DohClient(IReadOnlyDictionary<string, string>? bootstrapMap = null)
    {
        _http = CreateDefaultClient(bootstrapMap);
    }

    /// <inheritdoc/>
    public async Task<byte[]> QueryAsync(string serverUrl, byte[] queryBytes, CancellationToken ct = default)
    {
        var content = new ByteArrayContent(queryBytes);
        content.Headers.ContentType = DnsMessageContentType;

        using var request = new HttpRequestMessage(HttpMethod.Post, serverUrl)
        {
            Content = content
        };
        request.Headers.Accept.Add(DnsMessageAccept);

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"DoH 回應錯誤: HTTP {(int)response.StatusCode}");

        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private static HttpClient CreateDefaultClient(IReadOnlyDictionary<string, string>? bootstrapMap)
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(5),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };

        // 若有 bootstrap map，用 ConnectCallback 直連 IP，不讓 HttpClient 做 DNS 解析
        // 這樣 DoH 請求就不會被 WinDivert 攔截，避免遞迴鎖死
        if (bootstrapMap != null && bootstrapMap.Count > 0)
        {
            handler.ConnectCallback = async (context, ct) =>
            {
                string host = context.DnsEndPoint.Host;
                int port = context.DnsEndPoint.Port;

                if (bootstrapMap.TryGetValue(host, out var bootstrapIp))
                {
                    // 直接連到 bootstrap IP，不做 DNS 查詢
                    var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
                    {
                        NoDelay = true
                    };
                    try
                    {
                        await socket.ConnectAsync(new IPEndPoint(IPAddress.Parse(bootstrapIp), port), ct);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                }

                // 未知 host（如自訂伺服器）：用系統預設連線（可能觸發 DNS）
                var fallbackSocket = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp)
                {
                    DualMode = true,
                    NoDelay = true
                };
                try
                {
                    await fallbackSocket.ConnectAsync(context.DnsEndPoint, ct);
                    return new NetworkStream(fallbackSocket, ownsSocket: true);
                }
                catch
                {
                    fallbackSocket.Dispose();
                    throw;
                }
            };
        }

        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
    }
}
