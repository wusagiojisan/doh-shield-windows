namespace DohShield.Core.Dns;

/// <summary>
/// DoH 主要伺服器的 Circuit Breaker
/// 移植自 Android CircuitBreaker.kt
///
/// 狀態流程：
///   Closed → (連續失敗 N 次) → Open → (等待 recoveryTimeoutMs) → HalfOpen
///   HalfOpen + 成功 → Closed（恢復）
///   HalfOpen + 失敗 → Open（重置等待時間）
/// </summary>
public sealed class CircuitBreaker
{
    public enum State { Closed, Open, HalfOpen }

    private readonly int _failureThreshold;
    private readonly long _recoveryTimeoutMs;
    private readonly Func<long> _currentTimeMs;
    private readonly object _lock = new();

    private State _state = State.Closed;
    private int _failureCount;
    private long _openedAt;

    public State CurrentState
    {
        get { lock (_lock) return _state; }
    }

    public long RecoveryTimeoutMs => _recoveryTimeoutMs;

    public CircuitBreaker(
        int failureThreshold = 5,
        long recoveryTimeoutMs = 30_000L,
        Func<long>? currentTimeMs = null)
    {
        _failureThreshold = failureThreshold;
        _recoveryTimeoutMs = recoveryTimeoutMs;
        _currentTimeMs = currentTimeMs ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    /// <summary>
    /// 是否應跳過主要伺服器。
    /// 同時處理 Open → HalfOpen 的自動轉換。
    /// </summary>
    public bool ShouldSkipPrimary()
    {
        lock (_lock)
        {
            if (_state == State.Open && _currentTimeMs() - _openedAt >= _recoveryTimeoutMs)
                _state = State.HalfOpen;

            return _state == State.Open;
        }
    }

    /// <summary>Closed 狀態才允許重試（HalfOpen 只給一次機會）</summary>
    public bool ShouldRetry()
    {
        lock (_lock) return _state == State.Closed;
    }

    public void RecordSuccess()
    {
        lock (_lock)
        {
            _state = State.Closed;
            _failureCount = 0;
        }
    }

    public void RecordFailure()
    {
        lock (_lock)
        {
            switch (_state)
            {
                case State.HalfOpen:
                    Trip();
                    break;
                case State.Closed:
                    _failureCount++;
                    if (_failureCount >= _failureThreshold)
                        Trip();
                    break;
                case State.Open:
                    break;
            }
        }
    }

    private void Trip()
    {
        _state = State.Open;
        _openedAt = _currentTimeMs();
        _failureCount = 0;
    }
}
