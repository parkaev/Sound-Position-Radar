namespace SoundRadar.Core.Dsp;

/// <summary>
/// Карта каналов → углы динамиков. 0° = спереди, положительные = справа, ±180 = сзади.
/// Порядок каналов — стандартный WAVEFORMATEX: FL, FR, FC, LFE, BL, BR, SL, SR.
/// Стерео обрабатывается отдельно (constant-power pan law, ±90°) — векторная сумма по двум
/// углам ±30° не различает стороны.
/// </summary>
public sealed class ChannelMap
{
    public static readonly ChannelMap Mono = new([0]);
    public static readonly ChannelMap Quad = new([-30, 30, -135, 135]);
    public static readonly ChannelMap FiveOne = new([-30, 30, 0, Skip, -120, 120]);
    public static readonly ChannelMap SevenOne = new([-30, 30, 0, Skip, -135, 135, -90, 90]);

    /// <summary>Метка канала, не участвующего в направлении (LFE).</summary>
    public const double Skip = double.NaN;

    private ChannelMap(double[] anglesDeg)
    {
        AnglesDeg = anglesDeg;
        HasRear = anglesDeg.Any(a => !double.IsNaN(a) && Math.Abs(a) > 90.0 + 1e-9);
    }

    /// <summary>Угол каждого канала, ° (0 = перед, ±180 = зад; NaN — канал игнорируется).</summary>
    public double[] AnglesDeg { get; }

    /// <summary>Есть ли в карте каналы позади слушателя (|угол| &gt; 90°).</summary>
    public bool HasRear { get; }

    public bool SupportsDirection360 => HasRear || AnglesDeg.Count(a => !double.IsNaN(a)) > 2;

    public static ChannelMap For(int channels) => channels switch
    {
        1 => Mono,
        4 => Quad,
        6 => FiveOne,
        8 => SevenOne,
        _ => Mono, // 2 и экзотика обрабатываются Analyzer'ом отдельно (stereo ILD / downmix)
    };
}
