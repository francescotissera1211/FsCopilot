namespace FsCopilot.Network;

/// <summary>
/// Exponential moving average packet loss tracker.
/// Works with both percentage-based (P2P via LiteNetLib) and count-based (Relay) inputs.
/// </summary>
internal sealed class PacketLossTracker
{
    private const float Alpha = 0.3f;          // smoothing factor (higher = more weight to recent)
    private const float MinWeight = 0.01f;      // prevent stale values from lasting forever
    
    private float _ema;
    private int _samples;

    /// <summary>
    /// Record loss from a percentage value (0-100). Used by P2P.
    /// </summary>
    public float RecordPercent(float percent)
    {
        if (_samples == 0)
            _ema = percent;
        else
            _ema = _ema * (1f - Alpha) + percent * Alpha;
        
        _samples++;
        
        // Decay to prevent stale values from persisting forever
        if (_samples > 100)
        {
            _samples = 50;
            // EMA stays, just reset counter
        }
        
        return GetLoss();
    }

    /// <summary>
    /// Record loss from sent/lost counts. Used by Relay ping tracking.
    /// </summary>
    public float Record(int sent, int lost)
    {
        var percent = sent == 0 ? 0f : (float)lost / sent * 100f;
        return RecordPercent(percent);
    }

    /// <summary>
    /// Get current smoothed loss percentage (0-100).
    /// </summary>
    public float GetLoss() => _ema;

    /// <summary>
    /// Reset all statistics.
    /// </summary>
    public void Reset()
    {
        _ema = 0f;
        _samples = 0;
    }
}