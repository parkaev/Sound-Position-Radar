using SoundRadar.Core.Dsp;
using SoundRadar.Core.Events;
using Xunit;

namespace SoundRadar.Core.Tests;

/// <summary>
/// Тесты профиля DayZ: узкий пан (±90° → ILD ~±5 дБ, замерено по записям) и буст детекции
/// вч-полос (щелчок пули). Проверяем калибровочную кривую и частотный порог.
/// </summary>
public class DayZProfileTests
{
    private const int Rate = 48000;
    private const int ChunkFrames = 960;

    [Fact]
    public void DayZCalibration_SideSource_MapsToNinety()
    {
        var (hub, ended) = NewHub();
        // Замер по DayZ: источник строго сбоку (±90°) → ILD ≈ ±15 дБ и больше (жёсткий пан,
        // отношение каналов ограничено фоном противоположного уха).
        var aL = 0.06;
        var aR = aL * Math.Pow(10, 0.75); // ILD +15 дБ
        var analyzer = new Analyzer(hub, WithWarmup(AnalyzerOptions.DayZ()));

        FeedStereo(analyzer, 3.0, (rnd, _) => (float)(Noise(rnd) * aL), (rnd, _) => (float)(Noise(rnd) * aR), 11);
        FeedStereo(analyzer, 0.4, (rnd, _) => (float)(Noise(rnd) * aL * 6), (rnd, _) => (float)(Noise(rnd) * aR * 6), 12);
        FeedStereo(analyzer, 1.5, (rnd, _) => (float)(Noise(rnd) * aL), (rnd, _) => (float)(Noise(rnd) * aR), 13);

        var ev = Assert.Single(ended);
        Assert.InRange(ev.AzimuthDeg, 84.0, 90.0);
    }

    [Fact]
    public void GenericOptions_SameSource_FollowsConstantPower()
    {
        var (hub, ended) = NewHub();
        var aL = 0.06;
        var aR = aL * Math.Pow(10, 0.75);
        var analyzer = new Analyzer(hub, WithWarmup(new AnalyzerOptions()));

        FeedStereo(analyzer, 3.0, (rnd, _) => (float)(Noise(rnd) * aL), (rnd, _) => (float)(Noise(rnd) * aR), 21);
        FeedStereo(analyzer, 0.4, (rnd, _) => (float)(Noise(rnd) * aL * 6), (rnd, _) => (float)(Noise(rnd) * aR * 6), 22);
        FeedStereo(analyzer, 1.5, (rnd, _) => (float)(Noise(rnd) * aL), (rnd, _) => (float)(Noise(rnd) * aR), 23);

        var ev = Assert.Single(ended);
        // constant-power: (4/π)·atan(√10^1.5)−1 → ≈72°
        Assert.InRange(ev.AzimuthDeg, 67.0, 77.0);
    }

    [Fact]
    public void DayZBoost_HfCrackOverThreshold_Detected()
    {
        var (hub, ended) = NewHub();
        var analyzer = new Analyzer(hub, WithWarmup(AnalyzerOptions.DayZ()));

        FeedStereo(analyzer, 3.0, (rnd, _) => (float)(Noise(rnd) * 0.002), (rnd, _) => (float)(Noise(rnd) * 0.002), 31);
        // Щелчок пули: вч-синус 8 кГц (полоса 6к+), превышение ~+7 дБ над фоном —
        // ниже обычного порога 9, но буст +4 для вч-полосы должен его поймать.
        FeedTone(analyzer, 0.3, 8000, 0.0032, 32);
        FeedStereo(analyzer, 1.5, (rnd, _) => (float)(Noise(rnd) * 0.002), (rnd, _) => (float)(Noise(rnd) * 0.002), 33);

        Assert.Single(ended);
    }

    [Fact]
    public void GenericOptions_HfCrackBelowThreshold_Missed()
    {
        var (hub, ended) = NewHub();
        var analyzer = new Analyzer(hub, WithWarmup(new AnalyzerOptions()));

        FeedStereo(analyzer, 3.0, (rnd, _) => (float)(Noise(rnd) * 0.002), (rnd, _) => (float)(Noise(rnd) * 0.002), 41);
        FeedTone(analyzer, 0.3, 8000, 0.0032, 42);
        FeedStereo(analyzer, 1.5, (rnd, _) => (float)(Noise(rnd) * 0.002), (rnd, _) => (float)(Noise(rnd) * 0.002), 43);

        Assert.Empty(ended);
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static (EventHub hub, List<SoundEvent> ended) NewHub()
    {
        var hub = new EventHub();
        var ended = new List<SoundEvent>();
        hub.EventEnded += e => ended.Add(e);
        return (hub, ended);
    }

    private static AnalyzerOptions WithWarmup(AnalyzerOptions o)
    {
        o.WarmupSec = 2.0;
        return o;
    }

    private static double Noise(Random rnd) => rnd.NextDouble() * 2 - 1;

    private static void FeedStereo(
        Analyzer analyzer, double seconds,
        Func<Random, int, float> genL, Func<Random, int, float> genR, int seed)
    {
        var rnd = new Random(seed);
        var chunk = new float[ChunkFrames * 2];
        var frames = (int)(seconds * Rate);
        var written = 0;
        while (written < frames)
        {
            var take = Math.Min(ChunkFrames, frames - written);
            for (var i = 0; i < take; i++)
            {
                chunk[2 * i] = genL(rnd, written + i);
                chunk[2 * i + 1] = genR(rnd, written + i);
            }
            analyzer.ProcessSamples(take < ChunkFrames ? chunk[..(take * 2)] : chunk, 2, Rate);
            written += take;
        }
    }

    private static void FeedTone(Analyzer analyzer, double seconds, double freq, double amplitude, int seed)
    {
        // Синус со случайной фазовой дрожью, чтобы не выглядел «идеальным» сигналом.
        var rnd = new Random(seed);
        var phase = rnd.NextDouble() * Math.PI * 2;
        FeedStereo(analyzer, seconds,
            (_, f) => (float)(amplitude * Math.Sin(2 * Math.PI * freq * f / Rate + phase)),
            (_, f) => (float)(amplitude * Math.Sin(2 * Math.PI * freq * f / Rate + phase)),
            seed);
    }
}
