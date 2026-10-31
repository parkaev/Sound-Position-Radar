using SoundRadar.Core.Dsp;
using SoundRadar.Core.Events;
using Xunit;

namespace SoundRadar.Core.Tests;

/// <summary>
/// Холодный старт: звук приходит сразу (фон у планки). Регрессия на дедлок: событие замораживало
/// фоновый трекер, фон никогда не выучивался, событие никогда не кончалось.
/// </summary>
public class ColdStartTests
{
    private const int Rate = 48000;
    private const int ChunkFrames = 960;

    [Fact]
    public void ColdStart_ShotThenQuiet_SecondBurstStillDetected()
    {
        var hub = new EventHub();
        var ended = new List<SoundEvent>();
        hub.EventEnded += e => ended.Add(e);
        var analyzer = new Analyzer(hub, new AnalyzerOptions { WarmupSec = 0 });

        FeedBurst(analyzer, 0.3, 0.06, 45, 1);      // сразу с выстрела, фон не выучен
        FeedQuiet(analyzer, 8.0, 0.002, 2);         // долгая тишина: фон должен выучиться
        FeedBurst(analyzer, 0.3, 0.06, -45, 3);     // второй выстрел обязан детектироваться
        FeedQuiet(analyzer, 2.0, 0.002, 4);

        Assert.True(ended.Count >= 2, $"событий: {ended.Count}");
        var last = ended[^1];
        Assert.InRange(last.AzimuthDeg, -53.0, -37.0);
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
