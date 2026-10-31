using SoundRadar.Core.Dsp;
using SoundRadar.Core.Events;
using Xunit;

namespace SoundRadar.Core.Tests;

/// <summary>
/// Синтетические сквозные тесты детектора: известный пан → известный угол.
/// Энергетическая модель: белый шум; при амплитуде 0.002 энергия полос сидит на ~30 дБ
/// ниже бёрста 0.06, фон успевает сойтись за прогрев (WarmupSec=2, быстрый τ).
/// Порядок каналов 7.1: FL, FR, FC, LFE, BL, BR, SL, SR.
/// </summary>
public class AnalyzerTests
{
    private const int Rate = 48000;
    private const int ChunkFrames = 960; // 20 мс

    // ---- стерео (180°, ILD) -----------------------------------------------------------------

    [Fact]
    public void Stereo_PannedBurst_DetectedAtCorrectAzimuth()
    {
        var (hub, ended) = NewHub();
        var analyzer = new Analyzer(hub);

        FeedQuiet(analyzer, 3.0, 0.002, 11);
        FeedBurst(analyzer, 0.4, 0.06, 45, 22);
        FeedQuiet(analyzer, 1.5, 0.002, 33);

        var ev = Assert.Single(ended);
        Assert.InRange(ev.AzimuthDeg, 37.0, 53.0);
        Assert.True(ev.SalienceDb > 10, $"salience {ev.SalienceDb:F1} dB");
        Assert.True(ev.FrontalOnly, "стерео всегда frontal-only");
    }

    [Fact]
    public void Stereo_LeftOnlyBurst_DetectedFullyLeft()
    {
        var (hub, ended) = NewHub();
        var analyzer = new Analyzer(hub);

        FeedQuiet(analyzer, 2.0, 0.002, 41);
        FeedBurst(analyzer, 0.4, 0.06, -90, 42);
        FeedQuiet(analyzer, 1.5, 0.002, 43);

        var ev = Assert.Single(ended);
        Assert.InRange(ev.AzimuthDeg, -90.0, -80.0);
        Assert.True(ev.FrontalOnly);
    }

    [Fact]
    public void Stereo_QuietBackground_ProducesNoEvents()
    {
        var (hub, ended) = NewHub();
        var analyzer = new Analyzer(hub);

        FeedQuiet(analyzer, 6.0, 0.002, 51);

        Assert.Empty(ended);
    }

    [Fact]
    public void Stereo_TwoBurstsWithShortGap_MergeIntoOneEvent()
    {
        var (hub, ended) = NewHub();
        var analyzer = new Analyzer(hub);

        FeedQuiet(analyzer, 2.0, 0.002, 61);
        FeedBurst(analyzer, 0.3, 0.06, 0, 62);
        FeedQuiet(analyzer, 0.1, 0.002, 63);
        FeedBurst(analyzer, 0.3, 0.06, 0, 64);
        FeedQuiet(analyzer, 1.5, 0.002, 65);

        Assert.Single(ended);
    }

    // ---- 7.1 (360°, вектор по углам каналов) --------------------------------------------------

    [Fact]
    public void SevenOne_RearBurst_DetectedBehind()
    {
        var (hub, ended) = NewHub();
        var analyzer = new Analyzer(hub);

        // 7.1: FL, FR, FC, LFE, BL, BR, SL, SR — бёрст сзади (BL+BR)
        FeedMulti(analyzer, 3.0, 8, Quiet8(), 0.002, 71);
        FeedMulti(analyzer, 0.4, 8, Burst8(4, 5), 0.06, 72);
        FeedMulti(analyzer, 1.5, 8, Quiet8(), 0.002, 73);

        var ev = Assert.Single(ended);
        AssertAngleNear(ev.AzimuthDeg, 180.0, 15.0);
        Assert.False(ev.FrontalOnly, "задние каналы несли энергию события");
    }

    [Fact]
    public void SevenOne_BackLeftBurst_DetectedAt135()
    {
        var (hub, ended) = NewHub();
        var analyzer = new Analyzer(hub);

        FeedMulti(analyzer, 3.0, 8, Quiet8(), 0.002, 81);
        FeedMulti(analyzer, 0.4, 8, Burst8(4), 0.06, 82); // только BL (−135°)
        FeedMulti(analyzer, 1.5, 8, Quiet8(), 0.002, 83);

        var ev = Assert.Single(ended);
        AssertAngleNear(ev.AzimuthDeg, -135.0, 15.0);
        Assert.False(ev.FrontalOnly);
    }

    [Fact]
    public void SevenOne_SideLeftBurst_DetectedAtMinus90()
    {
        var (hub, ended) = NewHub();
        var analyzer = new Analyzer(hub);

        FeedMulti(analyzer, 3.0, 8, Quiet8(), 0.002, 91);
        FeedMulti(analyzer, 0.4, 8, Burst8(6), 0.06, 92); // только SL (−90°)
        FeedMulti(analyzer, 1.5, 8, Quiet8(), 0.002, 93);

        var ev = Assert.Single(ended);
        AssertAngleNear(ev.AzimuthDeg, -90.0, 15.0);
    }

    [Fact]
    public void SevenOne_FrontBurst_IsFrontalOnly()
    {
        var (hub, ended) = NewHub();
        var analyzer = new Analyzer(hub);

        FeedMulti(analyzer, 3.0, 8, Quiet8(), 0.002, 101);
        FeedMulti(analyzer, 0.4, 8, Burst8(0, 1), 0.06, 102); // FL+FR
        FeedMulti(analyzer, 1.5, 8, Quiet8(), 0.002, 103);

        var ev = Assert.Single(ended);
        AssertAngleNear(ev.AzimuthDeg, 0.0, 12.0);
        Assert.True(ev.FrontalOnly, "задние каналы тихие — событие фронтовое");
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static (EventHub hub, List<SoundEvent> ended) NewHub()
    {
        var hub = new EventHub();
        var ended = new List<SoundEvent>();
        hub.EventEnded += e => ended.Add(e);
        return (hub, ended);
    }

    private static double[] Quiet8() => [1, 1, 1, 1, 1, 1, 1, 1];

    /// <summary>Бёрстовые усиления 7.1 по индексам каналов.</summary>
    private static double[] Burst8(params int[] activeChannels)
    {
        var gains = new double[8];
        foreach (var c in activeChannels)
            gains[c] = 1.0 / Math.Sqrt(activeChannels.Length);
        return gains;
    }

    private static void AssertAngleNear(double actual, double expected, double tolerance)
    {
        var diff = Math.Abs(((actual - expected) % 360 + 540) % 360 - 180);
        Assert.InRange(diff, 0, tolerance);
    }

    private static void FeedQuiet(Analyzer analyzer, double seconds, double amplitude, int seed, int channels = 2)
    {
        var rnd = new Random(seed);
        Feed(analyzer, (int)(seconds * Rate), channels, () =>
        {
            var n = (float)((rnd.NextDouble() * 2 - 1) * amplitude);
            var buf = new float[channels];
            for (var c = 0; c < channels; c++)
                buf[c] = n;
            return buf;
        });
    }

    private static void FeedBurst(Analyzer analyzer, double seconds, double amplitude, double azimuthDeg, int seed, int channels = 2)
    {
        var rnd = new Random(seed);
        var (gL, gR) = PanMath.ChannelGains(azimuthDeg);
        Feed(analyzer, (int)(seconds * Rate), channels, () =>
        {
            var n = (rnd.NextDouble() * 2 - 1) * amplitude;
            var buf = new float[channels];
            buf[0] = (float)(n * gL);
            buf[1] = (float)(n * gR);
            return buf;
        });
    }

    private static void FeedMulti(Analyzer analyzer, double seconds, int channels, double[] gains, double amplitude, int seed)
    {
        var rnd = new Random(seed);
        Feed(analyzer, (int)(seconds * Rate), channels, () =>
        {
            var n = (rnd.NextDouble() * 2 - 1) * amplitude;
            var buf = new float[channels];
            for (var c = 0; c < channels; c++)
                buf[c] = (float)(n * gains[c]);
            return buf;
        });
    }

    private static void Feed(Analyzer analyzer, int frames, int channels, Func<float[]> gen)
    {
        var chunk = new float[ChunkFrames * channels];
        var written = 0;
        while (written < frames)
        {
            var take = Math.Min(ChunkFrames, frames - written);
            for (var i = 0; i < take; i++)
            {
                var s = gen();
                Array.Copy(s, 0, chunk, i * channels, channels);
            }
            analyzer.ProcessSamples(
                take < ChunkFrames ? chunk[..(take * channels)] : chunk, channels, Rate);
            written += take;
        }
    }
}
