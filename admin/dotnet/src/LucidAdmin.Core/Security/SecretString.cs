namespace LucidAdmin.Core.Security;

/// <summary>
/// A string wrapper that prevents accidental logging or serialization of secret values.
/// ToString() returns "[REDACTED]". JSON serialization writes "[REDACTED]".
/// The actual value is only accessible via explicit .Reveal() call.
/// Implements IDisposable to null the reference on cleanup.
/// </summary>
public sealed class SecretString : IDisposable
{
    private string? _value;

    /// <summary>
    /// Creates a new SecretString wrapping the given value.
    /// </summary>
    public SecretString(string value) => _value = value;

    /// <summary>
    /// Explicitly retrieve the secret value. Use sparingly and never log the result.
    /// </summary>
    public string Reveal() => _value ?? throw new ObjectDisposedException(nameof(SecretString));

    /// <summary>
    /// Returns "[REDACTED]" to prevent accidental logging.
    /// </summary>
    public override string ToString() => "[REDACTED]";

    /// <summary>
    /// Implicit conversion from string for convenience.
    /// </summary>
    public static implicit operator SecretString(string value) => new(value);

    /// <summary>
    /// Whether this instance has been disposed.
    /// </summary>
    public bool IsDisposed => _value == null;

    /// <summary>
    /// Nulls the reference to allow GC to collect the string.
    /// </summary>
    public void Dispose()
    {
        _value = null;
    }
}
