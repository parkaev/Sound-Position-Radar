using System.Windows.Media;

namespace SoundRadar.App;

/// <summary>Общая палитра индикации: режимы цвета и именованные оттенки.</summary>
internal static class RadarPalette
{
    /// <summary>Цвет по громкости события (классический зелёный → жёлтый → красный).</summary>
    public static Color SalienceColor(double salienceDb)
    {
        var t = Math.Clamp((salienceDb - 6) / 18, 0, 1);
        return t < 0.5
            ? Lerp(Color.FromRgb(0x4C, 0xAF, 0x50), Color.FromRgb(0xFF, 0xC1, 0x07), t * 2)
            : Lerp(Color.FromRgb(0xFF, 0xC1, 0x07), Color.FromRgb(0xF4, 0x43, 0x36), (t - 0.5) * 2);
    }

    /// <summary>
    /// Цвет по «близости»: зеленоватый — звук дальше, жёлтый — средне,
    /// красный — близко, тёмно-красный — очень близко. Диапазон растянут под реальные
    /// salience DayZ (~15…55 дБ превышения).
    /// </summary>
    public static Color ProximityColor(double salienceDb)
    {
        var t = Math.Clamp((salienceDb - 8.0) / 42.0, 0, 1);
        if (t < 0.35)
            return Lerp(Color.FromRgb(0x4C, 0xAF, 0x50), Color.FromRgb(0xFF, 0xC1, 0x07), t / 0.35);
        if (t < 0.7)
            return Lerp(Color.FromRgb(0xFF, 0xC1, 0x07), Color.FromRgb(0xF4, 0x43, 0x36), (t - 0.35) / 0.35);
        return Lerp(Color.FromRgb(0xF4, 0x43, 0x36), Color.FromRgb(0x8B, 0x00, 0x00), (t - 0.7) / 0.3);
    }

    /// <summary>Именованные оттенки для режима постоянного цвета.</summary>
    public static Color NamedColor(string name) => name switch
    {
        "Оранжевый" => Color.FromRgb(0xFF, 0x98, 0x00),
        "Жёлтый" => Color.FromRgb(0xFF, 0xEB, 0x3B),
        "Голубой" => Color.FromRgb(0x4F, 0xC3, 0xF7),
        "Белый" => Color.FromRgb(0xF5, 0xF5, 0xF5),
        _ => Color.FromRgb(0xF4, 0x43, 0x36), // Красный
    };

    public static Color Lerp(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));
}
