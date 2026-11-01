using SoundRadar.Core.Events;

namespace SoundRadar.Core.Dsp;

/// <summary>
/// Классификатор типа звука по накопленному профилю полос события. Вызывается на каждом
/// блоке анализа и перезаписывает тип: подпись маркера появляется сразу, к пику события
/// тип стабилизируется.
///
/// Правила выведены из контрольных записей DayZ (реплей: 27 событий выстрелов AK-74/CZ-550
/// + 10 событий шагов, бетон и трава, свои шаги игрока):
///   выстрел — пик превышения сидит в басах (&lt;150/150–500) или в верхах (6к+), середина
///   (0.5–2к) часто проседает; отделившийся щелчок пули — пик ТОЛЬКО в верхних полосах;
///   шаги — «долина» в 0.5–2к (на 7+ дБ ниже пика) при пике в 2–6к, без выраженного
///   вч-выброса и без пика в басах. Непрерывная ходьба склеивается в длинное событие,
///   поэтому длительность шагов не ограничивается.
/// Форма события описывается как «на сколько дБ полоса ниже пика события»
/// (rel = max - band) — не зависит от абсолютной громкости.
/// Правило шагов консервативно (7 из 10 событий шагов записей «бетон/трава», ноль ложных
/// на 25 громких событиях выстрелов; 2–3 события шагов спектрально неотличимы от выстрела):
/// лучше Unknown, чем ложная подпись. Уточняется по новым записям.
/// </summary>
public static class SoundClassifier
{
    /// <summary>Ниже этой громкости событие не классифицируем.</summary>
    public const double MinSalienceDb = 12.0;
    /// <summary>Насколько глубоко пик должен сидеть в басах/верхах, чтобы считать событие оружейным.</summary>
    public const double WeaponPeakMarginDb = 5.5;
    /// <summary>Насколько басы должны отставать от пика у чистого щелчка пули.</summary>
    public const double CrackBassGapDb = 8.0;

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

        // Шаги — две формы из записей (бетон/трава):
        //  1) «долина» в 0.5–2к (7+ дБ) при пике в 2–6к, без вч-выброса и без пика в басах;
        //  2) басовый удар ступни: пик в басах/низкой середине, но середина проседает и
        //     вч (6к+) НЕ выражены (у выстрелов с басовым пиком 6к+ отстаёт на 6+ дБ).
        // Проверяется до оружейного правила; на 27 событиях выстрелов ложных срабатываний нет.
        var lowPeak = Math.Min(rel[0], rel[1]);
        var stepsShape = rel[2] >= 7.0 && rel[3] <= 5.5 && rel[4] <= 5.5 && lowPeak >= 6.0;
        var stepsThump = rel[2] >= 5.5 && rel[3] <= 6.5 && rel[4] <= 5.0 && lowPeak <= 5.5;
        if (stepsShape || stepsThump)
            return (SoundEventType.Footsteps, 0.6);

        // Оружейный звук: пик в басах или в верхах (выстрел — слоистый, очередь — длинная).
        var weaponPeak = Math.Min(rel[0], Math.Min(rel[1], rel[4]));
        if (weaponPeak <= WeaponPeakMarginDb)
            return (SoundEventType.Shot, Math.Clamp(1.0 - weaponPeak / WeaponPeakMarginDb * 0.5, 0.5, 1.0));

        return (SoundEventType.Unknown, 0);
    }
}
