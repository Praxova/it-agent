using System.Security.Cryptography;
using System.Text;
using LucidAdmin.Core.Entities;
using LucidAdmin.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LucidAdmin.Infrastructure.Services;

public class AuditChainService
{
    /// <summary>The genesis previous-hash value for the very first audit record.</summary>
    public const string GenesisInputString = "PRAXOVA-AUDIT-GENESIS-v1";

    public static readonly string GenesisHash =
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(GenesisInputString)))
               .ToLowerInvariant();

    private readonly LucidDbContext _db;
    private readonly ILogger<AuditChainService> _logger;

    // Single lock serializes concurrent audit writes within a process
    private static readonly SemaphoreSlim _writeLock = new(1, 1);

    public AuditChainService(LucidDbContext db, ILogger<AuditChainService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Insert an audit record with hash chain fields computed and set.
    /// Serialized via a process-level lock to ensure monotonic sequence numbers.
    /// </summary>
    public async Task InsertAsync(AuditEvent auditEvent, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var tx = await _db.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable, ct);

            // Get last sequence number and hash
            var lastRecord = await _db.AuditEvents
                .OrderByDescending(e => e.SequenceNumber)
                .Select(e => new { e.SequenceNumber, e.RecordHash })
                .FirstOrDefaultAsync(ct);

            var previousHash = lastRecord?.RecordHash ?? GenesisHash;
            var nextSequence = (lastRecord?.SequenceNumber ?? 0) + 1;

            auditEvent.SequenceNumber = nextSequence;
            auditEvent.PreviousRecordHash = previousHash;
            auditEvent.RecordHash = ComputeRecordHash(auditEvent);

            _db.AuditEvents.Add(auditEvent);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to insert audit record — chain may be broken");
            throw;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Compute the SHA-256 hash of an audit record's canonical content.
    /// All fields except RecordHash itself are included.
    /// </summary>
    public static string ComputeRecordHash(AuditEvent e)
    {
        // Canonical string: fields joined with | delimiter, nulls as empty string
        var canonical = string.Join("|",
            e.SequenceNumber.ToString(),
            e.CreatedAt.ToString("O"),   // ISO 8601 round-trip
            e.AgentId?.ToString() ?? "",
            e.Action.ToString(),
            e.TicketNumber ?? "",
            e.CapabilityId ?? "",
            e.ToolServerId?.ToString() ?? "",
            e.Success ? "Success" : "Failure",
            e.DetailsJson ?? "",
            e.PreviousRecordHash
        );

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
                      .ToLowerInvariant();
    }

    /// <summary>
    /// Verify the hash chain for a range of records.
    /// Records with SequenceNumber == 0 predate the chain and are excluded.
    /// </summary>
    public async Task<AuditVerificationReport> VerifyAsync(
        long? fromSequence = null,
        long? toSequence = null,
        CancellationToken ct = default)
    {
        var query = _db.AuditEvents
            .Where(e => e.SequenceNumber > 0)   // Skip pre-integrity records
            .OrderBy(e => e.SequenceNumber)
            .AsQueryable();

        if (fromSequence.HasValue)
            query = query.Where(e => e.SequenceNumber >= fromSequence.Value);
        if (toSequence.HasValue)
            query = query.Where(e => e.SequenceNumber <= toSequence.Value);

        var records = await query.ToListAsync(ct);

        var report = new AuditVerificationReport
        {
            RecordsChecked = records.Count,
            VerifiedAt = DateTime.UtcNow
        };

        if (records.Count == 0)
        {
            report.Verified = true;
            report.ChainIntact = true;
            return report;
        }

        report.FirstSequence = records[0].SequenceNumber;
        report.LastSequence = records[^1].SequenceNumber;

        string? expectedPreviousHash = null;
        long? expectedSequence = null;

        foreach (var record in records)
        {
            // Check sequence continuity
            if (expectedSequence.HasValue && record.SequenceNumber != expectedSequence.Value)
            {
                for (var gap = expectedSequence.Value; gap < record.SequenceNumber; gap++)
                    report.GapsDetected.Add(gap);
            }
            expectedSequence = record.SequenceNumber + 1;

            // Verify PreviousRecordHash
            if (expectedPreviousHash != null && record.PreviousRecordHash != expectedPreviousHash)
            {
                report.HashMismatches.Add(new HashMismatch
                {
                    Sequence = record.SequenceNumber,
                    Expected = expectedPreviousHash,
                    Actual = record.PreviousRecordHash,
                    Detail = "PreviousRecordHash mismatch"
                });
            }

            // Verify RecordHash
            var computedHash = ComputeRecordHash(record);
            if (record.RecordHash != computedHash)
            {
                report.HashMismatches.Add(new HashMismatch
                {
                    Sequence = record.SequenceNumber,
                    Expected = computedHash,
                    Actual = record.RecordHash,
                    Detail = "RecordHash mismatch — record content was modified"
                });
            }

            expectedPreviousHash = record.RecordHash;
        }

        report.ChainIntact = report.GapsDetected.Count == 0 && report.HashMismatches.Count == 0;
        report.Verified = report.ChainIntact;
        return report;
    }
}

public class AuditVerificationReport
{
    public bool Verified { get; set; }
    public int RecordsChecked { get; set; }
    public long FirstSequence { get; set; }
    public long LastSequence { get; set; }
    public bool ChainIntact { get; set; }
    public List<long> GapsDetected { get; set; } = new();
    public List<HashMismatch> HashMismatches { get; set; } = new();
    public DateTime VerifiedAt { get; set; }
}

public class HashMismatch
{
    public long Sequence { get; set; }
    public string Expected { get; set; } = "";
    public string Actual { get; set; } = "";
    public string Detail { get; set; } = "";
}
