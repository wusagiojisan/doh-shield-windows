namespace DohShield.Core.Dns;

/// <summary>
/// DNS 快取中的一筆記錄
/// </summary>
/// <param name="ResponseBytes">raw DNS response bytes</param>
/// <param name="ExpiresAt">過期時間（DateTimeOffset UTC）</param>
public sealed record DnsCacheEntry(byte[] ResponseBytes, DateTimeOffset ExpiresAt)
{
    public bool IsExpired() => DateTimeOffset.UtcNow > ExpiresAt;
}
