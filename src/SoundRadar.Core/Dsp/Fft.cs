namespace SoundRadar.Core.Dsp;

/// <summary>
/// Итеративный radix-2 FFT (in-place, forward). Зависимость не тянем: 1024-точечного
/// комплексного FFT на блок хватает за глаза (десятки микросекунд).
/// </summary>
public sealed class Fft
{
    private readonly int n;
    private readonly int levels;
    private readonly double[] cosTable;
    private readonly double[] sinTable;
    private readonly int[] reversed;

    public int Size => n;

    public Fft(int size)
    {
        if (size <= 0 || (size & (size - 1)) != 0)
            throw new ArgumentException("Размер FFT должен быть степенью двойки.", nameof(size));
        n = size;
        levels = BitLength(size) - 1;
        cosTable = new double[n / 2];
        sinTable = new double[n / 2];
        for (var i = 0; i < n / 2; i++)
        {
            cosTable[i] = Math.Cos(2 * Math.PI * i / n);
            sinTable[i] = Math.Sin(2 * Math.PI * i / n);
        }
        reversed = new int[n];
        for (var i = 0; i < n; i++)
            reversed[i] = ReverseBits(i);
    }

    public void Forward(double[] re, double[] im)
    {
        for (var i = 0; i < n; i++)
        {
            var j = reversed[i];
            if (j > i)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        for (var half = 1; half < n; half *= 2)
        {
            var step = n / (half * 2);
            for (var i = 0; i < n; i += half * 2)
            {
                for (var j = i; j < i + half; j++)
                {
                    var k = (j - i) * step;
                    var l = j + half;
                    var tre = re[l] * cosTable[k] + im[l] * sinTable[k];
                    var tim = -re[l] * sinTable[k] + im[l] * cosTable[k];
                    re[l] = re[j] - tre;
                    im[l] = im[j] - tim;
                    re[j] += tre;
                    im[j] += tim;
                }
            }
        }
    }

    private int ReverseBits(int x)
    {
        var result = 0;
        for (var i = 0; i < levels; i++)
        {
            result = (result << 1) | (x & 1);
            x >>= 1;
        }
        return result;
    }

    private static int BitLength(int v)
    {
        var len = 0;
        while (v > 0)
        {
            len++;
            v >>= 1;
        }
        return len;
    }
}
