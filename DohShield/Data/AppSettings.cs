using System.IO;
using System.Text.Json;
using DohShield.Core.Network;

namespace DohShield.Data;

/// <summary>
/// 應用程式設定（JSON 持久化）
/// 儲存路徑：{AppDirectory}/settings.json
/// </summary>
public sealed class AppSettings
{
    private static readonly string SettingsPath =
        Path.Combine(AppContext.BaseDirectory, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    // ──────── 設定屬性 ────────

    /// <summary>選擇的 DoH 伺服器 ID（"cloudflare" / "google" / "quad9" / "custom"）</summary>
    public string ServerId { get; set; } = "cloudflare";

    /// <summary>自訂 DoH 伺服器 URL（ServerId=="custom" 時生效）</summary>
    public string CustomServerUrl { get; set; } = "";

    // ──────── 計算屬性 ────────

    /// <summary>目前生效的伺服器 URL</summary>
    public string ActiveServerUrl =>
        ServerId == "custom"
            ? CustomServerUrl
            : DohServers.FindById(ServerId).Url;

    /// <summary>Fallback 伺服器（主要為 Cloudflare 時用 Google，否則用 Cloudflare）</summary>
    public string FallbackServerUrl =>
        ActiveServerUrl == DohServers.Cloudflare.Url
            ? DohServers.Google.Url
            : DohServers.Cloudflare.Url;

    /// <summary>目前伺服器的顯示名稱</summary>
    public string ActiveServerDisplayName =>
        DohServers.UrlToDisplayName(ActiveServerUrl);

    // ──────── I/O ────────

    public static async Task<AppSettings> LoadAsync()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new AppSettings();

            await using var fs = File.OpenRead(SettingsPath);
            return await JsonSerializer.DeserializeAsync<AppSettings>(fs, JsonOptions)
                   ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public async Task SaveAsync()
    {
        await using var fs = File.Create(SettingsPath);
        await JsonSerializer.SerializeAsync(fs, this, JsonOptions);
    }
}
