using SoundRadar.Core.Dsp;
using Xunit;

namespace SoundRadar.Core.Tests;

public class BackgroundTrackerTests
{
    [Fact]
    public void SteadyEnergy_TracksLevel()
    {
        var tracker = new BackgroundTracker(tauRiseSec: 0.1, tauFallSec: 0.1, floorDb: -75.0);
        for (var i = 0; i < 100; i++)
            tracker.Update(1.0, 0.01, frozen: false);
        Assert.InRange(tracker.Level, 0.95, 1.05);
    }

    [Fact]
    public void Frozen_LevelDoesNotChange()
    {
        var tracker = new BackgroundTracker(0.1, 0.1, -75.0);
        var before = tracker.Level;
        tracker.Update(100.0, 0.01, frozen: true);
        Assert.Equal(before, tracker.Level);
    }

    [Fact]
    public void LevelNeverBelowFloor()
    {
        var tracker = new BackgroundTracker(0.1, 0.1, -40.0);
        tracker.Update(1e-30, 0.01, frozen: false);
        Assert.Equal(Math.Pow(10, -40.0 / 10.0), tracker.Level, 15);
    }
}
