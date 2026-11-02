using System.Collections.Concurrent;
using DropZone.Domain.Enums;
using DropZone.Domain.Models;

namespace DropZone.Application.Services;

public sealed class TransferQueue
{
    private readonly ConcurrentDictionary<Guid, TransferJob> _jobs = new();
    private readonly int _maxConcurrentTransfers;
    private readonly object _stateLock = new();

    public TransferQueue(int maxConcurrentTransfers = 3)
    {
        _maxConcurrentTransfers = Math.Max(1, maxConcurrentTransfers);
    }

    public bool TryEnqueue(TransferJob job)
    {
        return _jobs.TryAdd(job.Id, job);
    }

    public bool TryGet(Guid jobId, out TransferJob? job)
    {
        return _jobs.TryGetValue(jobId, out job);
    }

    public IReadOnlyList<TransferJob> GetAllJobs()
    {
        return _jobs.Values.OrderByDescending(j => j.CreatedAt).ToList();
    }

    public IReadOnlyList<TransferJob> GetActiveTransfers()
    {
        return _jobs.Values
            .Where(j => j.State == TransferState.Transferring || j.State == TransferState.Connecting)
            .ToList();
    }

    public bool CanStartNewTransfer()
    {
        return GetActiveTransfers().Count < _maxConcurrentTransfers;
    }

    public TransferJob? GetNextPendingJob()
    {
        lock (_stateLock)
        {
            if (!CanStartNewTransfer())
                return null;

            return _jobs.Values
                .Where(j => j.State == TransferState.Pending)
                .OrderBy(j => j.CreatedAt)
                .FirstOrDefault();
        }
    }

    public bool TryRemove(Guid jobId)
    {
        return _jobs.TryRemove(jobId, out _);
    }
}

