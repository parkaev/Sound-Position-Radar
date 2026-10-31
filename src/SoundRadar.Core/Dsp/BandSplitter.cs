namespace SoundRadar.Core.Dsp;

/// <summary>
/// Разбиение спектра FFT на частотные полосы. Грани выбраны так, чтобы шаги (150–2000 Гц),
/// выстрелы (широкополосные) и бас музыки (&lt;150 Гц) попадали в разные полосы — это даёт
/// маскирование музыки и будущую грубую классификацию звуков бесплатно.
/// </summary>
public sealed class BandSplitter
{
    /// <summary>Полосы по умолчанию (Гц), edges.Length - 1 полос.</summary>
    public static readonly double[] DefaultEdgesHz = { 0, 150, 500, 2000, 6000, 24000 };

    private readonly int[] binStart;
    private readonly int[] binEnd;

    public BandSplitter(double[] edgesHz, int fftSize, int sampleRate)
    {
        EdgesHz = edgesHz;
        var maxBin = fftSize / 2 - 1; // DC (k=0) исключаем
        binStart = new int[edgesHz.Length - 1];
        binEnd = new int[edgesHz.Length - 1];
        for (var b = 0; b < binStart.Length; b++)
        {
            var lo = Math.Max(1, (int)Math.Ceiling(edgesHz[b] * fftSize / sampleRate));
            var hi = Math.Min(maxBin, (int)Math.Ceiling(edgesHz[b + 1] * fftSize / sampleRate));
            if (hi <= lo)
                hi = Math.Min(maxBin, lo + 1); // очень узкая полоса — не оставляем пустой
            binStart[b] = lo;
            binEnd[b] = hi;
        }
    }

    public double[] EdgesHz { get; }

    public int BandCount => binStart.Length;

    public int BinStart(int band) => binStart[band];

    public int BinEnd(int band) => binEnd[band]; // exclusive

    public static string BandLabel(int band) => band switch
    {
        0 => "<150",
        1 => "150-500",
        2 => "0.5-2k",
        3 => "2-6k",
        4 => "6k+",
        _ => $"B{band}",
    };
}
