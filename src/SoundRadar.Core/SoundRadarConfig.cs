using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SoundRadar.Core;

/// <summary>
/// Пользовательские настройки: какой звук захватывать (процесс или всё устройство) и параметры
/// детектора. Хранится в %APPDATA%\SoundPositionRadar\config.json.
/// </summary>
public sealed class SoundRadarConfig
{
    /// <summary>"Process" (звук конкретного процесса) или "Device" (весь выход устройства).</summary>
    public string SourceType { get; set; } = "Process";

    /// <summary>Имя exe процесса-источника без расширения. Приоритетнее <see cref="ProcessId"/>:
    /// игры перезапускаются с новым PID, а имя остаётся.</summary>
    public string? ProcessName { get; set; }

    /// <summary>Последний выбранный PID (fallback, если процесс с таким именем не найден).</summary>
    public int? ProcessId { get; set; }

    public bool IncludeProcessTree { get; set; } = true;

    /// <summary>Профиль игры: "DayZ" (узкий пан, басовое направление, буст вч-щелчка)
    /// или "Generic" (constant-power). Выбор влияет на калибровку детектора.</summary>
    public string GameProfile { get; set; } = "DayZ";

    /// <summary>Пытаться захватывать 8 каналов (7.1): игры с surround-миксом дают честный зад (360°).
    /// Если система не отдаёт 8 каналов — автоматический откат на стерео (180°).
    /// По умолчанию выключено: большинство игр рендерят стерео, а лишний запрос формата
    /// без пользы только добавляет точку отказа.</summary>
    public bool PreferMultichannel { get; set; } = false;

    public double ThresholdDb { get; set; } = 9.0;

    public double MinSalienceDb { get; set; } = 6.0;

    /// <summary>Показывать оверлей-индикатор поверх игр (прототип M2).</summary>
    public bool OverlayEnabled { get; set; }

    /// <summary>Скрывать в оверлее события в центральных зонах (±1 зона от «перед») —
    /// устарело, заменено на OverlayHiddenZones; используется только для миграции старых конфигов.</summary>
    public bool OverlayHideCenter { get; set; }

    /// <summary>Скрытые зоны оверлея — индексы через запятую (0 = перед, N/2 = зад).</summary>
    public string OverlayHiddenZones { get; set; } = "";

    /// <summary>Общая непрозрачность оверлея, 0…1.</summary>
    public double OverlayOpacity { get; set; } = 0.85;

    /// <summary>Размер свечения от края экрана, px (пресет «Кромка»); для «Дуг» — обратный
    /// отступ дуг от края.</summary>
    public double OverlaySizeDepth { get; set; } = 130.0;

    /// <summary>Угловая полуширина реакции события, °.</summary>
    public double OverlayHalfWidth { get; set; } = 16.0;

    /// <summary>Время затухания метки, с.</summary>
    public double OverlayFadeSec { get; set; } = 1.0;

    /// <summary>Показывать журнал событий в правой колонке тест-окна.</summary>
    public bool ShowEventLog { get; set; } = true;

    /// <summary>Тема оформления: "System" (как в Windows), "Dark" или "Light".</summary>
    public string Theme { get; set; } = "System";

    /// <summary>Версия, напоминание об обновлении до которой скрыто («не показывать снова»).</summary>
    public string SkipUpdateVersion { get; set; } = "";

    /// <summary>Пресет отображения оверлея: "Дуги" (секторы у краёв) или "Кромка (градиент)"
    /// (свечение вдоль краёв экрана, прозрачно к центру).</summary>
    public string OverlayPreset { get; set; } = "Дуги";

    /// <summary>Режим цвета оверлея: "Frequency" (по частотам: бас — тёмно-красный, верха —
    /// зелёный) или "Fixed" (постоянный оттенок).</summary>
    public string OverlayColorMode { get; set; } = "Frequency";

    /// <summary>Оттенок для режима постоянного цвета.</summary>
    public string OverlayColorName { get; set; } = "Красный";

    /// <summary>Глобальная горячая клавиша включения/отключения оверлея, напр. "Ctrl+Alt+R".
    /// Пустая строка — не задана.</summary>
    public string OverlayHotkey { get; set; } = "";

    /// <summary>Число зон по кругу в оверлее (8/12/16/24): ширина подсвечиваемого сектора = 360/N.</summary>
    public int OverlayZones { get; set; } = 16;

    /// <summary>Скрытые зоны показывают приглушённый маркер (видно, что звучите), а не скрываются полностью.</summary>
    public bool OverlayDimZones { get; set; } = true;

    /// <summary>Индекс монитора для оверлея (0 = основной). Список — System.Windows.Forms.Screen.AllScreens.</summary>
    public int OverlayMonitor { get; set; } = 0;

    [JsonIgnore]
    public bool IsProcessSource => string.Equals(SourceType, "Process", StringComparison.OrdinalIgnoreCase);

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SoundPositionRadar",
        "config.json");

    public static SoundRadarConfig Load()
    {
        try
        {
            if (File.Exists(DefaultPath))
            {
                var cfg = JsonSerializer.Deserialize<SoundRadarConfig>(File.ReadAllText(DefaultPath)) ?? new SoundRadarConfig();
                // Миграция: старый тумблер «скрывать центр» → скрытая зона 0 (перед).
                if (cfg.OverlayHideCenter && string.IsNullOrEmpty(cfg.OverlayHiddenZones))
                    cfg.OverlayHiddenZones = "0";
                cfg.OverlayHideCenter = false;
                // Миграция: переименованный пресет оверлея.
                if (cfg.OverlayPreset == "Кромка (градиент)")
                    cfg.OverlayPreset = "Свечение краёв";
                // Миграция: режимы цвета «по близости»/«по громкости» заменены на «по частотам».
                if (cfg.OverlayColorMode is "Proximity" or "Salience")
                    cfg.OverlayColorMode = "Frequency";
                return cfg;
            }
        }
        catch
        {
            // Битый конфиг не должен валить приложение — начинаем с дефолтов.
        }
        return new SoundRadarConfig();
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(DefaultPath);
        if (dir is not null)
            Directory.CreateDirectory(dir);
        File.WriteAllText(DefaultPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
