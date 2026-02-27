using System.Collections.Concurrent;

namespace LucidAdmin.Web.Services;

/// <summary>
/// Simple in-memory sliding window rate limiter for operation token requests.
/// All state is ephemeral — a portal restart resets all counters.
/// </summary>
public class OperationTokenRateLimiter
{
    // Per-agent: 60 requests/minute
    private readonly ConcurrentDictionary<string, ConcurrentQueue<DateTime>> _perAgent = new();
    // Per-agent-capability: 30 requests/minute
    private readonly ConcurrentDictionary<string, ConcurrentQueue<DateTime>> _perAgentCapability = new();
    // Global: 120 requests/minute
    private readonly ConcurrentQueue<DateTime> _global = new();

    private const int PerAgentLimit = 60;
    private const int PerCapabilityLimit = 30;
    private const int GlobalLimit = 120;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Check and consume a rate limit slot. Returns true if allowed, false if rate limited.
    /// </summary>
    public bool TryConsume(string agentName, string capability)
    {
        var now = DateTime.UtcNow;
        var cutoff = now - Window;

        // Check global
        PruneOlderThan(_global, cutoff);
        if (_global.Count >= GlobalLimit) return false;

        // Check per-agent
        var agentQueue = _perAgent.GetOrAdd(agentName, _ => new ConcurrentQueue<DateTime>());
        PruneOlderThan(agentQueue, cutoff);
        if (agentQueue.Count >= PerAgentLimit) return false;

        // Check per-agent-capability
        var capKey = $"{agentName}:{capability}";
        var capQueue = _perAgentCapability.GetOrAdd(capKey, _ => new ConcurrentQueue<DateTime>());
        PruneOlderThan(capQueue, cutoff);
        if (capQueue.Count >= PerCapabilityLimit) return false;

        // Consume
        _global.Enqueue(now);
        agentQueue.Enqueue(now);
        capQueue.Enqueue(now);
        return true;
    }

    private static void PruneOlderThan(ConcurrentQueue<DateTime> queue, DateTime cutoff)
    {
        while (queue.TryPeek(out var oldest) && oldest < cutoff)
            queue.TryDequeue(out _);
    }
}
