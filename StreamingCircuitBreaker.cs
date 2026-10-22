namespace quasar_radio;

public enum StreamingCircuitState
{
    Closed,
    Open,
    HalfOpen
}

public sealed class StreamingCircuitBreaker
{
    private readonly object _sync = new();
    private readonly int _failureThreshold;
    private readonly TimeSpan _openDuration;
    private int _consecutiveFailures;
    private DateTime _openedAtUtc;
    private StreamingCircuitState _state = StreamingCircuitState.Closed;

    public StreamingCircuitBreaker(int failureThreshold, TimeSpan openDuration)
    {
        if (failureThreshold < 1) throw new ArgumentOutOfRangeException(nameof(failureThreshold));
        if (openDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(openDuration));

        _failureThreshold = failureThreshold;
        _openDuration = openDuration;
    }

    public StreamingCircuitState State
    {
        get
        {
            lock (_sync)
            {
                return _state;
            }
        }
    }
    public int ConsecutiveFailures
    {
        get
        {
            lock (_sync)
            {
                return _consecutiveFailures;
            }
        }
    }

    public TimeSpan OpenDuration => _openDuration;

    public bool TryEnter(out TimeSpan retryAfter)
    {
        lock (_sync)
        {
            retryAfter = TimeSpan.Zero;
            if (_state == StreamingCircuitState.Closed)
            {
                return true;
            }

            if (_state == StreamingCircuitState.HalfOpen)
            {
                return false;
            }

            retryAfter = _openDuration - (DateTime.UtcNow - _openedAtUtc);
            if (retryAfter > TimeSpan.Zero)
            {
                return false;
            }

            _state = StreamingCircuitState.HalfOpen;
            return true;
        }
    }

    public StreamingCircuitState RecordFailure()
    {
        lock (_sync)
        {
            _consecutiveFailures++;
            if (_state == StreamingCircuitState.HalfOpen || _consecutiveFailures >= _failureThreshold)
            {
                _state = StreamingCircuitState.Open;
                _openedAtUtc = DateTime.UtcNow;
            }
            else
            {
                _state = StreamingCircuitState.Closed;
            }

            return _state;
        }
    }

    public void RecordSuccess()
    {
        lock (_sync)
        {
            _consecutiveFailures = 0;
            _state = StreamingCircuitState.Closed;
        }
    }

    public void Reset() => RecordSuccess();
}