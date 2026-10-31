using SoundRadar.Core.Dsp;

namespace SoundRadar.Core.Events;

/// <summary>
/// Обнаруженный звук: направление, «выделяемость» над фоном, уверенность.
/// Поля обновляются на потоке анализа до <see cref="EventHub.EventEnded"/>.
/// </summary>
public sealed class SoundEvent
{
    public Guid Id { get; } = Guid.NewGuid();

    public DateTime StartedUtc { get; internal set; }

    public DateTime? EndedUtc { get; internal set; }

    /// <summary>Момент начала события по аудио-времени (сек от старта потока) — не зависит от
    /// скорости обработки (в оффлайн-реплее аудио течёт быстрее настенных часов).</summary>
    public double AudioStartSec { get; internal set; }

    /// <summary>Момент конца события по аудио-времени.</summary>
    public double? AudioEndSec { get; internal set; }

    /// <summary>Сглаженный азимут −180…+180° (0 = перед, −90 слева, +90 справа, ±180 сзади).
    /// Для стерео-микса диапазон фактически −90…+90 (зад не различим).</summary>
    public double AzimuthDeg { get; internal set; }

    /// <summary>Пик превышения над фоном, дБ.</summary>
    public double SalienceDb { get; internal set; }

    /// <summary>0…1 — насколько уверенно мы видим источник.</summary>
    public double Confidence { get; internal set; }

    /// <summary>Пиковое превышение над фоном по каждой полосе, дБ — база для будущей классификации.</summary>
    public double[] BandSalienceDb { get; } = new double[AnalyzerOptions.BandEdgesHz.Length - 1];

    /// <summary>Хоть один блок события нёс энергию в задних каналах (многоканальный микс).</summary>
    public bool RearSeen { get; internal set; }

    /// <summary>Зад не подтверждён: событие видно только спереди (стерео-микс или источник спереди).</summary>
    public bool FrontalOnly { get; internal set; } = true;

    public override string ToString() =>
        $"{StartedUtc:HH:mm:ss.fff}  {AzimuthDeg,7:F1}°  sal {SalienceDb,5:F1} dB  conf {Confidence:F2}{(FrontalOnly ? "  [фронт]" : "  [360]")}";
}

/// <summary>Снимок состояния анализатора на блоке — для метров UI.</summary>
public sealed record MeterFrame(
    double[] BandEnergyL,
    double[] BandEnergyR,
    double[] BandExceedDb,
    double[] BandBackgroundDb,
    int SampleRate,
    DateTime Utc,
    bool EventActive,
    int Channels,
    bool HasRearChannels,
    double RearRatioDb);
