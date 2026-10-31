namespace SoundRadar.Core.Dsp;

/// <summary>
/// Настройки детектора. Меняюаются на ходу (слайдеры UI) — анализатор читает поля на каждом блоке.
/// Значения — стартовые, тюнятся на реальных играх (см. профиль DayZ).
/// </summary>
public sealed class AnalyzerOptions
{
    public const int BlockSize = 1024;

    /// <summary>Грани полос, Гц (edges.Length - 1 полос).</summary>
    public static readonly double[] BandEdgesHz = BandSplitter.DefaultEdgesHz;

    /// <summary>Онсет: превышение над фоном, дБ.</summary>
    public double ThresholdDb { get; set; } = 9.0;

    /// <summary>Подтверждение онсета: блоков подряд (~21 мс каждый).</summary>
    public int MinBlocks { get; set; } = 2;

    /// <summary>Затухание: полоса считается активной, пока превышение выше этого, дБ.</summary>
    public double ReleaseDb { get; set; } = 4.0;

    /// <summary>Пауза между «тихими» участками, мс, при которой события склеиваются в одно.</summary>
    public double MergeGapMs { get; set; } = 200.0;

    /// <summary>Тишина дольше этого, мс → событие завершено.</summary>
    public double ReleaseMs { get; set; } = 300.0;

    /// <summary>Сколько полос должны уйти ниже порога затухания, чтобы событие считалось
    /// затихшим (защита от одной шумной полосы — птицы/ветер в вч — держащей событие вечно).</summary>
    public int QuietBandsRequired { get; set; } = 4;

    /// <summary>Максимальная длительность события, мс — принудительное завершение (страховка).</summary>
    public double EventMaxDurMs { get; set; } = 5000.0;

    /// <summary>Нижняя планка фона, дБ (относительно синуса полного масштаба) — защита от детекции в цифровой тишине.</summary>
    public double AbsFloorDb { get; set; } = -75.0;

    /// <summary>EMA фона: постоянная подъёма, с (фон медленно запоминает громкое).</summary>
    public double TauRiseSec { get; set; } = 4.0;

    /// <summary>EMA фона: постоянная спада, с.</summary>
    public double TauFallSec { get; set; } = 1.5;

    /// <summary>Сглаживание азимута внутри события (0…1, больше = живее).</summary>
    public double AzimuthAlpha { get; set; } = 0.4;

    /// <summary>События тише этого (пик превышения, дБ) не попадают в лог/UI.</summary>
    public double MinSalienceDb { get; set; } = 6.0;

    /// <summary>Прогрев после старта/сброса, с: фон ещё сходится, онсеты подавлены,
    /// иначе первые секунды дают шквал ложных событий.</summary>
    public double WarmupSec { get; set; } = 2.0;

    /// <summary>
    /// Веса полос для ОПРЕДЕЛЕНИЯ НАПРАВЛЕНИЯ. По данным DayZ направленная часть выстрела —
    /// басовый ствол (паннамирован), а вч-щелчок пули почти по центру: вч-полосы почти не
    /// должны тянуть угол.
    /// </summary>
    public double[] BandDirectionWeights { get; set; } = [1.0, 1.0, 1.0, 1.0, 1.0];

    /// <summary>
    /// Понижение порога онсета по полосам, дБ (положительное = полоса детектируется охотнее).
    /// Для тихих, но характерных звуков — вч-щелчок пули может быть всего +5…7 дБ над фоном.
    /// </summary>
    public double[] BandThresholdBoostDb { get; set; } = [0.0, 0.0, 0.0, 0.0, 0.0];

    /// <summary>
    /// Калибровка панорамы игры: точки (ILD дБ → угол°), по возрастанию ILD, симметричные.
    /// null = constant-power pan law (L=A·cos, R=A·sin).
    /// У DayZ пан сжат: источник строго сбоку (±90°) даёт ILD всего ~±5 дБ, диагональ ±45° ~±0.9 дБ.
    /// </summary>
    public (double IldDb, double AngleDeg)[]? IldCalibration { get; set; }

    /// <summary>Профиль DayZ: замерено по записям выстрелов AK-74/CZ-550 с известных позиций
    /// (окт 2026). Пан жёсткий: ±90° даёт ILD десятки дБ (ограничено фоном противоположного
    /// канала), диагональ ±45° ≈ ±6 дБ. Вч-щелчок пули детектируется охотнее (буст вч-полос).</summary>
    public static AnalyzerOptions DayZ() => new()
    {
        BandDirectionWeights = [1.0, 1.0, 0.8, 0.6, 0.4],
        BandThresholdBoostDb = [0.0, 0.0, 0.0, 2.0, 4.0],
        IldCalibration =
        [
            (-15.0, -90.0),
            (-6.2, -45.0),
            (0.0, 0.0),
            (6.2, 45.0),
            (15.0, 90.0),
        ],
    };

    public AnalyzerOptions Clone()
    {
        var copy = (AnalyzerOptions)MemberwiseClone();
        copy.BandDirectionWeights = (double[])BandDirectionWeights.Clone();
        copy.BandThresholdBoostDb = (double[])BandThresholdBoostDb.Clone();
        copy.IldCalibration = IldCalibration is null ? null : (ValueTuple<double, double>[])IldCalibration.Clone();
        return copy;
    }
}
