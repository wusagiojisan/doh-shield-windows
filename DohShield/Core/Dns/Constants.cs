namespace DohShield.Core.Dns;

internal static class Constants
{
    /// <summary>DNS LRU 快取最大筆數</summary>
    public const int DnsCacheMaxSize = 500;

    /// <summary>主要伺服器失敗後的重試延遲（毫秒）</summary>
    public const int DohRetryDelayMs = 100;

    /// <summary>HTTP 連線逾時（秒）</summary>
    public const int DohTimeoutSeconds = 5;
}
