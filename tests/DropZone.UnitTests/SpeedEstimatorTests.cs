using DropZone.Application.Services;
using Xunit;

namespace DropZone.UnitTests;

public class SpeedEstimatorTests
{
    [Fact]
    public void SpeedEstimator_InitialState_ReturnsZeroSpeedAndEta()
    {
        var estimator = new TransferSpeedEstimator();
        Assert.Equal(0.0, estimator.CurrentSpeedBytesPerSecond);
        Assert.Equal(0, estimator.CalculateEtaSeconds(1000000));
    }

    [Fact]
    public async Task SpeedEstimator_ProgressRecorded_ComputesValidSpeedAndEta()
    {
        var estimator = new TransferSpeedEstimator(windowSizeSeconds: 2);
        estimator.RecordProgress(0);

        await Task.Delay(150);
        estimator.RecordProgress(500_000); // 500KB

        var speed = estimator.CurrentSpeedBytesPerSecond;
        Assert.True(speed > 100_000.0, $"Speed {speed} should be > 100,000 B/s");

        var eta = estimator.CalculateEtaSeconds(10_000_000);
        Assert.True(eta >= 1 && eta <= 60, $"ETA {eta} should be between 1 and 60 seconds");
    }

    [Fact]
    public void SpeedEstimator_Reset_ClearsState()
    {
        var estimator = new TransferSpeedEstimator();
        estimator.RecordProgress(1000);
        estimator.Reset();

        Assert.Equal(0.0, estimator.CurrentSpeedBytesPerSecond);
        Assert.Equal(0, estimator.CalculateEtaSeconds(5000));
    }
}
