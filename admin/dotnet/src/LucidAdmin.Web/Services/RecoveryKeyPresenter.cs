namespace LucidAdmin.Web.Services;

/// <summary>
/// Holds a recovery key in memory for one-time display after initialization
/// or password change. The key is cleared after it is consumed.
/// Registered as singleton — only one pending key at a time.
/// </summary>
public class RecoveryKeyPresenter
{
    private readonly object _lock = new();
    private string? _pendingKey;

    /// <summary>Whether a recovery key is waiting to be displayed.</summary>
    public bool HasPendingKey
    {
        get { lock (_lock) { return _pendingKey != null; } }
    }

    /// <summary>Store a recovery key for one-time retrieval.</summary>
    public void SetKey(string recoveryKey)
    {
        lock (_lock)
        {
            _pendingKey = recoveryKey;
        }
    }

    /// <summary>
    /// Consume the pending recovery key. Returns the key and clears it.
    /// Returns null if no key is pending.
    /// </summary>
    public string? ConsumeKey()
    {
        lock (_lock)
        {
            var key = _pendingKey;
            _pendingKey = null;
            return key;
        }
    }
}
