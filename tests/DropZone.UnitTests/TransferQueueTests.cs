using DropZone.Application.Services;
using DropZone.Domain.Enums;
using DropZone.Domain.Models;
using Xunit;

namespace DropZone.UnitTests;

public class TransferQueueTests
{
    [Fact]
    public void TransferQueue_EnqueueAndRetrieve_WorksCorrectly()
    {
        var queue = new TransferQueue(maxConcurrentTransfers: 2);
        var job1 = new TransferJob { Id = Guid.NewGuid(), State = TransferState.Pending };
        var job2 = new TransferJob { Id = Guid.NewGuid(), State = TransferState.Pending };

        Assert.True(queue.TryEnqueue(job1));
        Assert.True(queue.TryEnqueue(job2));

        Assert.True(queue.TryGet(job1.Id, out var retrieved));
        Assert.NotNull(retrieved);
        Assert.Equal(job1.Id, retrieved.Id);

        Assert.Equal(2, queue.GetAllJobs().Count);
    }

    [Fact]
    public void TransferQueue_CanStartNewTransfer_RespectsMaxConcurrency()
    {
        var queue = new TransferQueue(maxConcurrentTransfers: 2);
        var job1 = new TransferJob { Id = Guid.NewGuid(), State = TransferState.Transferring };
        var job2 = new TransferJob { Id = Guid.NewGuid(), State = TransferState.Transferring };
        var job3 = new TransferJob { Id = Guid.NewGuid(), State = TransferState.Pending };

        queue.TryEnqueue(job1);
        queue.TryEnqueue(job2);
        queue.TryEnqueue(job3);

        Assert.Equal(2, queue.GetActiveTransfers().Count);
        Assert.False(queue.CanStartNewTransfer());

        // Complete job 1
        job1.State = TransferState.Completed;
        Assert.True(queue.CanStartNewTransfer());

        var next = queue.GetNextPendingJob();
        Assert.NotNull(next);
        Assert.Equal(job3.Id, next.Id);
    }
}

