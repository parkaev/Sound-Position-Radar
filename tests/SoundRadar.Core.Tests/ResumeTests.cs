using SoundRadar.Core.Dsp;
using SoundRadar.Core.Events;
using Xunit;

namespace SoundRadar.Core.Tests;

/// <summary>
/// Регрессия: возобновлённое (склеенное по MergeGap) событие оставляло EndedUtc установленным —
/// оверлей считал его завершённым и игнорировал новую позицию до полного затухания старой дуги.
/// </summary>
public class ResumeTests
{
    private const int Rate = 48000;
    private const int ChunkFrames = 960;

    [Fact]
    public void ResumedEvent_ClearsEndedUtc_AndUpdatesAzimuth()
    {
        var hub = new EventHub();
        var ended = new List<SoundEvent>();
        hub.EventEnded += e => ended.Add(e);
        var analyzer = new Analyzer(hub, new AnalyzerOptions
        {
            WarmupSec = 0,
            MergeGapMs = 3000, // большое окно склейки: второй выстрел гарантированно возобновит событие
        });

        // Выстрел 1 справа; тишина (событие завершается); выстрел 2 слева в пределах MergeGap —
        // событие возобновляется, азимут должен обновиться на новую позицию.
        FeedBurst(analyzer, 0.3, 0.06, 90, 1);
        FeedQuiet(analyzer, 3.0, 0.002, 2);
        FeedBurst(analyzer, 0.25, 0.06, -90, 3);

        // На пике второго выстрела: событие активно (не «закончено») и смотрит влево.
        var live = hub.LatestEvent;
        Assert.NotNull(live);
        Assert.True(live.EndedUtc is null,
            "возобновлённое событие обязано быть активным (EndedUtc сброшен)");
        Assert.InRange(live.AzimuthDeg, -90.0, -70.0);

        FeedQuiet(analyzer, 2.0, 0.002, 4);
        Assert.True(ended.Count >= 1);
    }

    private static double Noise(Random rnd) => rnd.NextDouble() * 2 - 1;

    private static void FeedBurst(Analyzer analyzer, double seconds, double amplitude, double azimuthDeg, int seed)
    {
        var rnd = new Random(seed);
        var (gL, gR) = PanMath.ChannelGains(azimuthDeg);
        Feed(analyzer, (int)(seconds * Rate), () =>
        {
            var n = Noise(rnd) * amplitude;
            return ((float)(n * gL), (float)(n * gR));
        });
    }

    private static void FeedQuiet(Analyzer analyzer, double seconds, double amplitude, int seed)
    {
        var rnd = new Random(seed);
        Feed(analyzer, (int)(seconds * Rate), () =>
        {
            var n = Noise(rnd) * amplitude;
            return ((float)n, (float)n);
        });
    }

    private static void Feed(Analyzer analyzer, int frames, Func<(float L, float R)> gen)
    {
        var chunk = new float[ChunkFrames * 2];
        var written = 0;
        while (written < frames)
        {
            var take = Math.Min(ChunkFrames, frames - written);
            for (var i = 0; i < take; i++)
            {
                var (l, r) = gen();
                chunk[2 * i] = l;
                chunk[2 * i + 1] = r;
            }
            analyzer.ProcessSamples(take < ChunkFrames ? chunk[..(take * 2)] : chunk, 2, Rate);
            written += take;
        }
    }
}
