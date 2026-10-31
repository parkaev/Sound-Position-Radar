namespace SoundRadar.Core.Dsp;

/// <summary>
/// Математика стерео-панорамы. Игры рендерят 3D-звук в стерео по constant-power закону
/// (L=A·cos, R=A·sin); из баланса энергий каналов восстанавливаем азимут −90…+90°.
/// Ограничение принципа: перед/зад и выше/ниже стерео-баланс не различает.
/// </summary>
public static class PanMath
{
    /// <summary>
    /// Азимут в градусах: −90 = полностью слева, 0 = центр, +90 = полностью справа.
    /// θ = 90° · ((4/π)·atan2(√E_R, √E_L) − 1).
    /// </summary>
    public static double AzimuthFromEnergies(double energyLeft, double energyRight)
    {
        if (energyLeft <= 0 && energyRight <= 0)
            return 0;
        var p = 4.0 / Math.PI * Math.Atan2(Math.Sqrt(Math.Max(0, energyRight)), Math.Sqrt(Math.Max(0, energyLeft))) - 1.0;
        return 90.0 * Math.Clamp(p, -1.0, 1.0);
    }

    /// <summary>Амплитуды каналов для источника на азимуте <paramref name="azimuthDeg"/> (constant-power).</summary>
    public static (double Left, double Right) ChannelGains(double azimuthDeg)
    {
        var p = Math.Clamp(azimuthDeg / 90.0, -1.0, 1.0);
        var angle = (p + 1.0) * Math.PI / 4.0;
        return (Math.Cos(angle), Math.Sin(angle));
    }
}
