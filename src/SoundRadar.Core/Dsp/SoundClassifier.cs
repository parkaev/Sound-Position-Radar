using SoundRadar.Core.Events;

namespace SoundRadar.Core.Dsp;

/// <summary>
/// Классификатор типа звука по накопленному профилю полос события. Вызывается на каждом
/// блоке анализа и перезаписывает тип: подпись маркера появляется сразу, к пику события
/// тип стабилизируется.
///
/// Правила выведены из контрольных записей DayZ (27 событий, replay датасета AK-74/CZ-550):
/// у выстрела пик превышения сидит в басах (&lt;150/150–500) или в верхах (6к+) — чаще всего
/// обе крайности, середина (0.5–2к) проседает; отделившийся щелчок пули — событие с пиком
/// ТОЛЬКО в верхних полосах. Форма события описывается как «на сколько дБ полоса ниже
/// пика события» (rel = max - band), что не зависит от абсолютной громкости.
/// Шаги: пик в середине, без басов и без вч-щелчка; правило консервативное — лучше
/// Unknown, чем ложная подпись. Эталон будет уточнён по записям шагов.
/// </summary>
public static class SoundClassifier
{
    /// <summary>Ниже этой громкости событие не классифицируем.</summary>
    public const double MinSalienceDb = 12.0;
    /// <summary>Насколько глубоко пик должен сидеть в басах/верхах, чтобы считать событие оружейным.</summary>
    public const double WeaponPeakMarginDb = 5.5;
    /// <summary>Насколько басы должны отставать от пика у чистого щелчка пули.</summary>
    public const double CrackBassGapDb = 8.0;
    /// <summary>Максимальная длительность события-шагов.</summary>
    public const double FootstepMaxDurMs = 1200.0;

    public static (SoundEventType Type, double Confidence) Classify(double[] bandSalienceDb, double durationMs)
    {
        if (bandSalienceDb.Length < 5)
            return (SoundEventType.Unknown, 0);
        var max = double.NegativeInfinity;
        foreach (var v in bandSalienceDb)
            if (v > max)
                max = v;
        if (double.IsNegativeInfinity(max) || max < MinSalienceDb)
            return (SoundEventType.Unknown, 0);

        // rel[b] = на сколько дБ полоса b ниже пика события.
        var rel = new double[bandSalienceDb.Length];
        for (var b = 0; b < rel.Length; b++)
            rel[b] = max - bandSalienceDb[b];

        // Щелчок пули: пик в 6к+, басы и низ середины заметно отстают.
        if (rel[4] <= 1.0 && rel[0] >= CrackBassGapDb && rel[1] >= CrackBassGapDb)
            return (SoundEventType.Click, Math.Clamp(rel[0] / 16.0, 0.5, 1.0));

        // Оружейный звук: пик в басах или в верхах (выстрел — слоистый, очередь — длинная).
        var weaponPeak = Math.Min(rel[0], Math.Min(rel[1], rel[4]));
        if (weaponPeak <= WeaponPeakMarginDb)
            return (SoundEventType.Shot, Math.Clamp(1.0 - weaponPeak / WeaponPeakMarginDb * 0.5, 0.5, 1.0));

        // Шаги: пик в середине (0.5–6к), басы и вч отстают, событие короткое.
        var midPeak = Math.Min(rel[2], rel[3]);
        if (durationMs <= FootstepMaxDurMs && midPeak <= 5.0 && rel[0] >= 6.0 && rel[4] >= 9.0)
            return (SoundEventType.Footsteps, 0.6);

        return (SoundEventType.Unknown, 0);
    }
}
