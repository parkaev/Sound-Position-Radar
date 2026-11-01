using System.IO;
using System.Text.Json;

namespace SoundRadar.Core.Events;

/// <summary>
/// Журнал событий в JSONL — для отладки, реплеев и автотестов по логу.
/// Одна строка на завершённое событие; слишком тихие (ниже MinSalienceDb) не пишутся.
/// </summary>
public sealed class JsonlEventLogger : IDisposable
{
    private readonly StreamWriter writer;
    private readonly double minSalienceDb;
    private readonly object gate = new();

    public JsonlEventLogger(string path, double minSalienceDb)
    {
        this.minSalienceDb = minSalienceDb;
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        writer = new StreamWriter(path, append: false) { AutoFlush = true };
    }

    public void Attach(EventHub hub) => hub.EventEnded += OnEventEnded;

    public void Detach(EventHub hub) => hub.EventEnded -= OnEventEnded;

    private void OnEventEnded(SoundEvent e)
    {
        if (e.SalienceDb < minSalienceDb)
            return;
        lock (gate)
        {
            writer.WriteLine(JsonSerializer.Serialize(new
            {
                type = "sound_event",
                id = e.Id,
                startedUtc = e.StartedUtc,
                endedUtc = e.EndedUtc,
                azimuthDeg = Math.Round(e.AzimuthDeg, 1),
                salienceDb = Math.Round(e.SalienceDb, 1),
                confidence = Math.Round(e.Confidence, 2),
                kind = e.Type.ToString().ToLowerInvariant(),
                kindConfidence = Math.Round(e.TypeConfidence, 2),
                frontalOnly = e.FrontalOnly,
                bandSalienceDb = e.BandSalienceDb.Select(v => Math.Round(v, 1)).ToArray(),
            }));
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            writer.Dispose();
        }
    }
}
