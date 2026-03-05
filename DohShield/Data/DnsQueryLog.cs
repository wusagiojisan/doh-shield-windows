namespace DohShield.Data;

/// <summary>
/// DNS 查詢紀錄資料模型（對應 SQLite dns_query_log 表）
/// 移植自 Android DnsQueryLogEntity.kt
/// </summary>
public sealed class DnsQueryLog
{
    public long Id { get; set; }
    public string Domain { get; set; } = "";
    public int Type { get; set; }           // A=1, AAAA=28 等
    public bool Cached { get; set; }
    public long ResponseTimeMs { get; set; }
    public string ResolvedBy { get; set; } = "";
    public long Timestamp { get; set; }     // Unix epoch milliseconds

    /// <summary>將 Type number 轉換為顯示字串</summary>
    public string TypeName => Type switch
    {
        1 => "A",
        28 => "AAAA",
        5 => "CNAME",
        15 => "MX",
        16 => "TXT",
        33 => "SRV",
        65 => "HTTPS",
        _ => Type.ToString()
    };

    /// <summary>時間戳轉換為本地 DateTime</summary>
    public DateTime LocalTime =>
        DateTimeOffset.FromUnixTimeMilliseconds(Timestamp).LocalDateTime;
}
