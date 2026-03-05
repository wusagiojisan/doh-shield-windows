using DohShield.Core.Dns;
using Xunit;

namespace DohShield.Tests;

public class CircuitBreakerTests
{
    [Fact]
    public void InitialState_IsClosed()
    {
        var cb = new CircuitBreaker();
        Assert.Equal(CircuitBreaker.State.Closed, cb.CurrentState);
    }

    [Fact]
    public void ShouldSkipPrimary_WhenClosed_ReturnsFalse()
    {
        var cb = new CircuitBreaker();
        Assert.False(cb.ShouldSkipPrimary());
    }

    [Fact]
    public void RecordFailures_ExceedsThreshold_TransitionsToOpen()
    {
        var cb = new CircuitBreaker(failureThreshold: 3);
        cb.RecordFailure();
        cb.RecordFailure();
        Assert.Equal(CircuitBreaker.State.Closed, cb.CurrentState);

        cb.RecordFailure();
        Assert.Equal(CircuitBreaker.State.Open, cb.CurrentState);
    }

    [Fact]
    public void ShouldSkipPrimary_WhenOpen_ReturnsTrue()
    {
        var cb = new CircuitBreaker(failureThreshold: 1);
        cb.RecordFailure();

        Assert.True(cb.ShouldSkipPrimary());
    }

    [Fact]
    public void RecordSuccess_WhenOpen_TransitionsToClosed()
    {
        var cb = new CircuitBreaker(failureThreshold: 1);
        cb.RecordFailure();
        Assert.Equal(CircuitBreaker.State.Open, cb.CurrentState);

        cb.RecordSuccess();
        Assert.Equal(CircuitBreaker.State.Closed, cb.CurrentState);
    }

    [Fact]
    public void OpenToHalfOpen_AfterRecoveryTimeout()
    {
        long fakeTime = 0;
        var cb = new CircuitBreaker(failureThreshold: 1, recoveryTimeoutMs: 100, currentTimeMs: () => fakeTime);

        cb.RecordFailure();
        Assert.Equal(CircuitBreaker.State.Open, cb.CurrentState);

        // 還未到 recovery timeout
        fakeTime = 50;
        Assert.True(cb.ShouldSkipPrimary());
        Assert.Equal(CircuitBreaker.State.Open, cb.CurrentState);

        // 超過 recovery timeout
        fakeTime = 200;
        bool skip = cb.ShouldSkipPrimary();
        Assert.False(skip);
        Assert.Equal(CircuitBreaker.State.HalfOpen, cb.CurrentState);
    }

    [Fact]
    public void HalfOpen_FailureTransitionsToOpen()
    {
        long fakeTime = 0;
        var cb = new CircuitBreaker(failureThreshold: 1, recoveryTimeoutMs: 100, currentTimeMs: () => fakeTime);

        cb.RecordFailure(); // → Open
        fakeTime = 200;
        cb.ShouldSkipPrimary(); // → HalfOpen

        Assert.Equal(CircuitBreaker.State.HalfOpen, cb.CurrentState);

        cb.RecordFailure(); // → Open again
        Assert.Equal(CircuitBreaker.State.Open, cb.CurrentState);
    }

    [Fact]
    public void ShouldRetry_WhenClosed_ReturnsTrue()
    {
        var cb = new CircuitBreaker();
        Assert.True(cb.ShouldRetry());
    }

    [Fact]
    public void ShouldRetry_WhenHalfOpen_ReturnsFalse()
    {
        long fakeTime = 0;
        var cb = new CircuitBreaker(failureThreshold: 1, recoveryTimeoutMs: 100, currentTimeMs: () => fakeTime);

        cb.RecordFailure(); // → Open
        fakeTime = 200;
        cb.ShouldSkipPrimary(); // → HalfOpen

        Assert.False(cb.ShouldRetry());
    }
}
