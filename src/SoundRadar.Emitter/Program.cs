using NAudio.Wave;

// SoundRadar.Emitter — тестовая утилита: играет панорамированные шумы-бёрсты на заданных углах.
// Позволяет проверять детектор без запуска игры: включить тест-окно с захватом устройства (или
// этого процесса) и сравнить показанные углы с заданными.
//
// Примеры:
//   SoundRadar.Emitter                                  // 0,45,-45,90 каждые 2 c, «шаги»
//   SoundRadar.Emitter --angles -90,90 --interval 3     // только лево/право
//   SoundRadar.Emitter --type shot --level 0.4          // широкополосные «выстрелы»

var angles = ParseAngles(GetArg(args, "--angles") ?? "0,45,-45,90");
var interval = double.Parse(GetArg(args, "--interval") ?? "2.0");
var level = double.Parse(GetArg(args, "--level") ?? "0.25");
var type = GetArg(args, "--type") ?? "step";
var count = int.Parse(GetArg(args, "--count") ?? "0");

Console.WriteLine($"Панорамы: [{string.Join(", ", angles)}]°, интервал {interval:F1} c, тип {type}, уровень {level:F2}");
var provider = new BurstProvider(angles, interval, level, type, count);
using var output = new WaveOutEvent();
output.Init(provider);
output.Play();
Console.WriteLine("Играет... Ctrl+C — выход.");
await Task.Delay(Timeout.Infinite);

static string? GetArg(string[] args, string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static double[] ParseAngles(string s) =>
    s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(double.Parse)
        .ToArray();

/// <summary>Генератор бёрстов: шумовая вспышка с огибающей, constant-power пан, лоу-пасс для «шагов».</summary>
internal sealed class BurstProvider : ISampleProvider
{
    private const int Rate = 48000;
    private readonly double[] angles;
    private readonly double intervalSec;
    private readonly double level;
    private readonly bool lowPass;
    private readonly int maxBursts;
    private readonly Random rnd = new();
    private readonly double burstSec = 0.35;
    private int angleIndex = -1;
    private int burstsDone;
    private int gapFrames;
    private int burstFrame;
    private bool bursting;
    private double lpL;
    private double lpR;

    public BurstProvider(double[] angles, double intervalSec, double level, string type, int maxBursts)
    {
        this.angles = angles;
        this.intervalSec = intervalSec;
        this.level = level;
        this.maxBursts = maxBursts;
        lowPass = !string.Equals(type, "shot", StringComparison.OrdinalIgnoreCase);
    }

    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(Rate, 2);

    public int Read(Span<float> buffer)
    {
        var frames = buffer.Length / 2;
        var gapTarget = (int)(intervalSec * Rate);
        var burstFrames = (int)(burstSec * Rate);
        // Коэффициент one-pole лоу-пасса ~2.5 кГц (шаги 150–2000 Гц, без «сибилянтов»).
        var a = 1.0 - Math.Exp(-2.0 * Math.PI * 2500.0 / Rate);

        for (var f = 0; f < frames; f++)
        {
            float l = 0, r = 0;

            if (maxBursts > 0 && burstsDone >= maxBursts)
            {
                buffer[2 * f] = 0;
                buffer[2 * f + 1] = 0;
                continue;
            }

            if (!bursting)
            {
                if (++gapFrames >= gapTarget)
                {
                    bursting = true;
                    burstFrame = 0;
                    lpL = lpR = 0;
                    angleIndex = (angleIndex + 1) % angles.Length;
                    burstsDone++;
                    Console.WriteLine($"→ бёрст #{burstsDone}: угол {angles[angleIndex]:+0;-0;0}°");
                }
            }
            else if (burstFrame >= burstFrames)
            {
                bursting = false;
                gapFrames = 0;
            }
            else
            {
                var t = burstFrame / (double)Rate;
                var attack = Math.Min(1.0, burstFrame / (0.004 * Rate));
                var env = attack * Math.Exp(-t / 0.10) * level;
                var n = (rnd.NextDouble() * 2 - 1) * env;
                var (gL, gR) = Pan(angles[angleIndex]);
                lpL += a * (n * gL - lpL);
                lpR += a * (n * gR - lpR);
                l = (float)(lowPass ? lpL : n * gL);
                r = (float)(lowPass ? lpR : n * gR);
                burstFrame++;
            }

            buffer[2 * f] = l;
            buffer[2 * f + 1] = r;
        }

        return frames * 2;
    }

    private static (double L, double R) Pan(double azimuthDeg)
    {
        var p = Math.Clamp(azimuthDeg / 90.0, -1.0, 1.0);
        var angle = (p + 1.0) * Math.PI / 4.0;
        return (Math.Cos(angle), Math.Sin(angle));
    }
}
