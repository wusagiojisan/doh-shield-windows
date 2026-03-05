using System.IO;
using Microsoft.Data.Sqlite;

namespace DohShield.Data;

/// <summary>
/// DNS 查詢紀錄 SQLite Repository
/// 移植自 Android DnsLogRepository / DnsLogRepositoryImpl
/// 資料庫路徑：{AppDirectory}/doh_shield.db
/// </summary>
public sealed class DnsLogRepository
{
    private readonly string _connectionString;

    public DnsLogRepository(string? dbPath = null)
    {
        string path = dbPath ?? Path.Combine(AppContext.BaseDirectory, "doh_shield.db");
        _connectionString = $"Data Source={path}";
    }

    /// <summary>初始化資料庫（建立表格）</summary>
    public async Task InitializeAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS dns_query_log (
                id               INTEGER PRIMARY KEY AUTOINCREMENT,
                domain           TEXT    NOT NULL,
                type             INTEGER NOT NULL,
                cached           INTEGER NOT NULL,
                response_time_ms INTEGER NOT NULL DEFAULT 0,
                resolved_by      TEXT    NOT NULL DEFAULT '',
                timestamp        INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_timestamp ON dns_query_log(timestamp DESC);
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>寫入一筆查詢紀錄</summary>
    public async Task LogQueryAsync(string domain, int type, bool cached, long responseTimeMs, string resolvedBy)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO dns_query_log (domain, type, cached, response_time_ms, resolved_by, timestamp)
            VALUES ($domain, $type, $cached, $responseTimeMs, $resolvedBy, $timestamp)
            """;
        cmd.Parameters.AddWithValue("$domain", domain);
        cmd.Parameters.AddWithValue("$type", type);
        cmd.Parameters.AddWithValue("$cached", cached ? 1 : 0);
        cmd.Parameters.AddWithValue("$responseTimeMs", responseTimeMs);
        cmd.Parameters.AddWithValue("$resolvedBy", resolvedBy);
        cmd.Parameters.AddWithValue("$timestamp", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>取得最近 N 筆紀錄（最新在前）</summary>
    public async Task<List<DnsQueryLog>> GetRecentLogsAsync(int limit = 200)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, domain, type, cached, response_time_ms, resolved_by, timestamp
            FROM dns_query_log
            ORDER BY timestamp DESC
            LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$limit", limit);

        var result = new List<DnsQueryLog>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new DnsQueryLog
            {
                Id = reader.GetInt64(0),
                Domain = reader.GetString(1),
                Type = reader.GetInt32(2),
                Cached = reader.GetInt32(3) != 0,
                ResponseTimeMs = reader.GetInt64(4),
                ResolvedBy = reader.GetString(5),
                Timestamp = reader.GetInt64(6)
            });
        }
        return result;
    }

    /// <summary>總查詢次數</summary>
    public async Task<long> GetTotalQueryCountAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM dns_query_log";
        var result = await cmd.ExecuteScalarAsync();
        return result is long l ? l : 0;
    }

    /// <summary>快取命中次數</summary>
    public async Task<long> GetCachedQueryCountAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM dns_query_log WHERE cached = 1";
        var result = await cmd.ExecuteScalarAsync();
        return result is long l ? l : 0;
    }

    /// <summary>非快取查詢的平均回應時間（ms）</summary>
    public async Task<double> GetAvgResponseTimeMsAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT AVG(response_time_ms) FROM dns_query_log WHERE cached = 0 AND response_time_ms > 0";
        var result = await cmd.ExecuteScalarAsync();
        if (result is null or DBNull) return 0;
        return Convert.ToDouble(result);
    }

    /// <summary>清除所有紀錄</summary>
    public async Task ClearAllAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM dns_query_log";
        await cmd.ExecuteNonQueryAsync();
    }
}
