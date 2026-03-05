namespace DohShield.Core.Network;

/// <summary>
/// DoH 伺服器定義
/// 移植自 Android DohServerConfig.kt / DohServers.kt
/// </summary>
public sealed record DohServerConfig(
    string Id,
    string Name,
    string Url,
    IReadOnlyList<string>? BootstrapIps = null
);

public static class DohServers
{
    public static readonly DohServerConfig Cloudflare = new(
        Id: "cloudflare",
        Name: "Cloudflare (1.1.1.1)",
        Url: "https://cloudflare-dns.com/dns-query",
        BootstrapIps: ["1.1.1.1", "1.0.0.1"]
    );

    public static readonly DohServerConfig Google = new(
        Id: "google",
        Name: "Google (8.8.8.8)",
        Url: "https://dns.google/dns-query",
        BootstrapIps: ["8.8.8.8", "8.8.4.4"]
    );

    public static readonly DohServerConfig Quad9 = new(
        Id: "quad9",
        Name: "Quad9 (9.9.9.9)",
        Url: "https://dns.quad9.net:5053/dns-query",
        BootstrapIps: ["9.9.9.9", "149.112.112.112"]
    );

    public static readonly DohServerConfig Custom = new(
        Id: "custom",
        Name: "自訂伺服器",
        Url: ""
    );

    /// <summary>顯示給使用者選擇的預設清單（不含 Custom）</summary>
    public static IReadOnlyList<DohServerConfig> Presets => [Cloudflare, Google, Quad9];

    /// <summary>將伺服器 URL 轉換為顯示名稱</summary>
    public static string UrlToDisplayName(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";
        var preset = Presets.FirstOrDefault(p => p.Url == url);
        if (preset != null) return preset.Name;
        try { return new Uri(url).Host; }
        catch { return url; }
    }

    /// <summary>由 ID 找出 preset 設定（找不到回傳 Custom）</summary>
    public static DohServerConfig FindById(string id)
        => Presets.FirstOrDefault(p => p.Id == id) ?? Custom;
}
