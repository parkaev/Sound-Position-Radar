namespace SoundRadar.Core.Dsp;

/// <summary>
/// Трекер фонового уровня энергии одной полосы. Фиксированный порог в играх не работает
/// (компрессия, музыка), поэтому фон — экспоненциальное скользящее среднее энергии
/// с разной постоянной на подъём (медленно запоминает громкое) и спад.
/// Пока в полосе идёт событие, обновление замораживается (флаг <c>frozen</c>), чтобы событие
/// не задрало свой же фон.
/// </summary>
public sealed class BackgroundTracker
{
    private const double FastTauSec = 0.25;
    private readonly double tauRiseSec;
    private readonly double tauFallSec;
    private readonly double floorEnergy;
    private double level;

    public BackgroundTracker(double tauRiseSec, double tauFallSec, double floorDb)
    {
        this.tauRiseSec = tauRiseSec;
        this.tauFallSec = tauFallSec;
        floorEnergy = Math.Pow(10, floorDb / 10.0);
        level = floorEnergy;
    }

    /// <summary>Текущий фоновый уровень (энергия, линейная).</summary>
    public double Level => level;

    public void Reset() => level = floorEnergy;

    /// <param name="frozen">Полоса участвует в событии — обновление заморожено.</param>
    /// <param name="fast">Прогрев: фон должен сойтись быстро, иначе первые секунды дают ложные превышения.</param>
    public void Update(double energy, double dtSec, bool frozen, bool fast = false)
    {
        if (frozen || dtSec <= 0)
            return;
        var tau = fast ? FastTauSec : energy > level ? tauRiseSec : tauFallSec;
        var alpha = 1.0 - Math.Exp(-dtSec / tau);
        level += alpha * (energy - level);
        if (level < floorEnergy)
            level = floorEnergy;
    }
}
