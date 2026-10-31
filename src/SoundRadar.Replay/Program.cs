using System.Globalization;
using System.Text;
using System.Text.Json;
using NAudio.Wave;
using SoundRadar.Core.Dsp;
using SoundRadar.Core.Events;

// SoundRadar.Replay — оффлайн-прогон WAV-записей через настоящий детектор.
// Аргументы: <manifest.json> <outDir>
// Манифест: [{ "file": "...", "label": "...", "angles": [0, -90, -180, -270] }, ...]
//   angles — ожидаемые углы источников по порядку появления (null/отсутствие = свип/свободный).
// Выход в outDir:
//   blocks.csv  — построчно метры каждого блока (энергии полос L/R, превышения, ILD)
//   events.jsonl — события детектора (+ накопленные ILD/профиль полос за событие)
//   summary.csv — сводка: ожидаемый угол (по порядку) vs обнаруженный, ошибка, salience

var manifestPath = args.Length > 0 ? args[0] : throw new ArgumentException("Укажите manifest.json");
var outDir = args.Length > 1 ? args[1] : "replay_out";
var genericProfile = args.Any(a => a == "--generic");
Directory.CreateDirectory(outDir);

var jsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
var entries = JsonSerializer.Deserialize<List<ManifestEntry>>(File.ReadAllText(manifestPath), jsonOpts)
              ?? throw new InvalidOperationException("Пустой манифест");

var blocksCsv = new StringBuilder("file,label,t_s,rate,ch,az,bg_db0,bg_db4,exceed0,exceed1,exceed2,exceed3,exceed4,e0l,e0r,e1l,e1r,e2l,e2r,e3l,e3r,e4l,e4r\r\n");
var eventsJsonl = new StringBuilder();
var summaryCsv = new StringBuilder("file,label,idx,expected_deg,detected_deg,err_deg,salience_db,conf,ild_total_db,ild_b0,ild_b1,ild_b2,ild_b3,ild_b4,dur_ms,t0_s\r\n");

foreach (var entry in entries)
{
    if (!File.Exists(entry.File))
    {
        Console.WriteLine($"!! файл не найден: {entry.File}");
        continue;
    }

    using var reader = new WaveFileReader(entry.File);
    var format = reader.WaveFormat;
    var sp = reader.ToSampleProvider();
    Console.WriteLine($"== {entry.Label}: {format.Channels} кан, {format.SampleRate} Гц, {reader.TotalTime.TotalSeconds:F1} c");

    var hub = new EventHub();
    var options = genericProfile ? new AnalyzerOptions() : AnalyzerOptions.DayZ();
    options.WarmupSec = 0; // записи начинаются сразу с выстрела — не съедать детекцию прогревом
    var analyzer = new Analyzer(hub, options);

    // События + пособытийный снимок накопленных энергий полос (L/R) на момент конца события.
    var events = new List<(SoundEvent ev, double[] acc, int blocks)>();
    SoundEvent? current = null;
    var acc = new double[10]; // e0l,e0r,...,e4l,e4r
    var accN = 0;
    hub.EventStarted += e => { current = e; Array.Clear(acc); accN = 0; };
    hub.MeterPublished += m =>
    {
        if (current is null || !m.EventActive)
            return;
        for (var b = 0; b < 5; b++)
        {
            acc[b * 2] += m.BandEnergyL[b];
            acc[b * 2 + 1] += m.BandEnergyR[b];
        }
        accN++;
    };
    hub.EventEnded += e =>
    {
        events.Add((e, (double[])acc.Clone(), accN));
        current = null;
    };

    var chunk = new float[960 * format.Channels];
    var tSec = 0.0;
    var frame = 0;
    while (true)
    {
        var read = sp.Read(chunk);
        if (read == 0)
            break;
        var frames = read / format.Channels;
        analyzer.ProcessSamples(chunk[..read], format.Channels, format.SampleRate);

        var m = hub.LatestMeter;
        if (m is not null)
        {
            var row = new List<string>
            {
                entry.Label, tSec.ToString("F3", CultureInfo.InvariantCulture),
                format.SampleRate.ToString(), format.Channels.ToString(),
                (hub.LatestEvent?.AzimuthDeg ?? double.NaN).ToString("F1", CultureInfo.InvariantCulture),
                m.BandBackgroundDb[0].ToString("F1", CultureInfo.InvariantCulture),
                m.BandBackgroundDb[4].ToString("F1", CultureInfo.InvariantCulture),
            };
            row.AddRange(m.BandExceedDb.Select(v => v.ToString("F1", CultureInfo.InvariantCulture)));
            for (var b = 0; b < 5; b++)
            {
                row.Add((10.0 * Math.Log10(m.BandEnergyL[b] + 1e-30)).ToString("F1", CultureInfo.InvariantCulture));
                row.Add((10.0 * Math.Log10(m.BandEnergyR[b] + 1e-30)).ToString("F1", CultureInfo.InvariantCulture));
            }
            blocksCsv.Append(entry.Label).Append(',').Append(string.Join(',', row)).Append("\r\n");
        }

        frame += frames;
        tSec = frame / (double)format.SampleRate;
    }

    // события → JSONL + сводка; сопоставление ожидаемый угол ↔ событие — жадное по углу.
    // Дедупликация: resume-цепочки могут добавлять один и тот же объект события несколько раз.
    var ordered = events
        .GroupBy(x => x.ev.Id)
        .Select(g => g.First())
        .OrderBy(x => x.ev.AudioStartSec)
        .ToList();
    var expectedByEvent = new Dictionary<int, double>();
    if (entry.Angles is { Length: > 0 })
    {
        var used = new HashSet<int>();
        foreach (var exp in entry.Angles)
        {
            int best = -1;
            var bestD = double.MaxValue;
            for (var i = 0; i < ordered.Count; i++)
            {
                if (used.Contains(i))
                    continue;
                var d = Math.Abs(Normalize(ordered[i].ev.AzimuthDeg - exp));
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }
            if (best >= 0 && bestD <= 90)
            {
                used.Add(best);
                expectedByEvent[best] = exp;
            }
        }
    }

    for (var i = 0; i < ordered.Count; i++)
    {
        var (e, eAcc, _) = ordered[i];
        var t0 = e.AudioStartSec;
        var dur = ((e.AudioEndSec ?? e.AudioStartSec) - e.AudioStartSec) * 1000.0;
        var ildB = new double[5];
        for (var b = 0; b < 5; b++)
            ildB[b] = 10.0 * Math.Log10((eAcc[b * 2 + 1] + 1e-30) / (eAcc[b * 2] + 1e-30));
        var ildTotal = 10.0 * Math.Log10((eAcc.Where((_, k) => k % 2 == 1).Sum() + 1e-30) / (eAcc.Where((_, k) => k % 2 == 0).Sum() + 1e-30));

        double? expected = expectedByEvent.TryGetValue(i, out var expVal) ? expVal : null;
        double? err = expected is null ? null : Normalize(e.AzimuthDeg - expected.Value);

        eventsJsonl.Append(JsonSerializer.Serialize(new
        {
            label = entry.Label,
            idx = i,
            expectedDeg = expected,
            t0 = Math.Round(t0, 3),
            durMs = Math.Round(dur),
            azimuthDeg = Math.Round(e.AzimuthDeg, 1),
            salienceDb = Math.Round(e.SalienceDb, 1),
            confidence = Math.Round(e.Confidence, 2),
            frontalOnly = e.FrontalOnly,
            ildTotalDb = Math.Round(ildTotal, 1),
            ildPerBand = ildB.Select(v => Math.Round(v, 1)),
            bandSalience = e.BandSalienceDb.Select(v => Math.Round(v, 1)),
        })).Append('\n');

        summaryCsv.Append(entry.Label).Append(',')
            .Append(i).Append(',')
            .Append(expected?.ToString("F0", CultureInfo.InvariantCulture) ?? "—").Append(',')
            .Append(e.AzimuthDeg.ToString("F1", CultureInfo.InvariantCulture)).Append(',')
            .Append(err?.ToString("F1", CultureInfo.InvariantCulture) ?? "—").Append(',')
            .Append(e.SalienceDb.ToString("F1", CultureInfo.InvariantCulture)).Append(',')
            .Append(e.Confidence.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
            .Append(ildTotal.ToString("F1", CultureInfo.InvariantCulture)).Append(',')
            .Append(string.Join(',', ildB.Select(v => v.ToString("F1", CultureInfo.InvariantCulture)))).Append(',')
            .Append(dur.ToString("F0", CultureInfo.InvariantCulture)).Append(',')
            .Append(t0.ToString("F2", CultureInfo.InvariantCulture)).Append("\r\n");
    }

    if (entry.Angles is { Length: > 0 })
        Console.WriteLine($"   сопоставлено {expectedByEvent.Count}/{entry.Angles.Length}, событий {ordered.Count}");
}

File.WriteAllText(Path.Combine(outDir, "blocks.csv"), blocksCsv.ToString(), Encoding.UTF8);
File.WriteAllText(Path.Combine(outDir, "events.jsonl"), eventsJsonl.ToString(), Encoding.UTF8);
File.WriteAllText(Path.Combine(outDir, "summary.csv"), summaryCsv.ToString(), Encoding.UTF8);
Console.WriteLine($"Готово: {outDir}\\{{blocks.csv,events.jsonl,summary.csv}}");

static double Normalize(double a)
{
    a %= 360.0;
    if (a > 180.0)
        a -= 360.0;
    if (a < -180.0)
        a += 360.0;
    return a;
}

internal sealed record ManifestEntry(string File, string Label, double[]? Angles);
