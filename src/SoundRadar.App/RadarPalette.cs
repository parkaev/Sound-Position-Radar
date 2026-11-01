using System.Windows.Media;

namespace SoundRadar.App;

/// <summary>Общая палитра индикации: режимы цвета и именованные оттенки.</summary>
internal static class RadarPalette
{
    /// <summary>
    /// Цвет по доминирующей полосе события: низкие частоты (бас) — тёмно-красный,
    /// высокие — зелёный. Позволяет на глаз отделять «низкие» звуки (выстрелы, техника)
    /// от «высоких» (щелчки, птицы, шаги по металлу).
    /// </summary>
    public static Color FrequencyColor(int band) => band switch
    {
        0 => Color.FromRgb(0x8F, 0x1D, 0x1D), // <150 Гц — тёмно-красный
        1 => Color.FromRgb(0xC4, 0x3C, 0x2C), // 150–500 — красный
        2 => Color.FromRgb(0xE0, 0x8A, 0x3C), // 0.5–2к — оранжевый
        3 => Color.FromRgb(0xA8, 0xC0, 0x4B), // 2–6к — жёлто-зелёный
        _ => Color.FromRgb(0x3F, 0xA8, 0x4F), // 6к+ — зелёный
    };

    /// <summary>Именованные оттенки для режима постоянного цвета.</summary>
    public static Color NamedColor(string name) => name switch
    {
        "Оранжевый" => Color.FromRgb(0xFF, 0x98, 0x00),
        "Жёлтый" => Color.FromRgb(0xFF, 0xEB, 0x3B),
        "Голубой" => Color.FromRgb(0x4F, 0xC3, 0xF7),
        "Белый" => Color.FromRgb(0xF5, 0xF5, 0xF5),
        _ => Color.FromRgb(0xF4, 0x43, 0x36), // Красный
    };
}
