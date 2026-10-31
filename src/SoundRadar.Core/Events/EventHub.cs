namespace SoundRadar.Core.Events;

/// <summary>
/// Шина событий между потоком анализа и потребителями (UI, логгер, будущий оверлей).
/// События Started/Ended приходят с потока анализа — подписчики сами маршалируют в UI-поток.
/// Для быстрых потребителей метрик есть «последний кадр» без очереди (UI опрашивает таймером).
/// </summary>
public sealed class EventHub
{
    public event Action<SoundEvent>? EventStarted;

    public event Action<SoundEvent>? EventEnded;

    /// <summary>Каждый обработанный блок (поток анализа) — для офлайн-реплея и отладки.</summary>
    public event Action<MeterFrame>? MeterPublished;

    private volatile SoundEvent? latestEvent;
    private volatile MeterFrame? latestMeter;

    /// <summary>Текущее (или последнее) событие.</summary>
    public SoundEvent? LatestEvent => latestEvent;

    /// <summary>Последний снимок метров (null, пока анализатор не получил ни одного блока).</summary>
    public MeterFrame? LatestMeter => latestMeter;

    public void RaiseStarted(SoundEvent e)
    {
        latestEvent = e;
        EventStarted?.Invoke(e);
    }

    public void RaiseEnded(SoundEvent e)
    {
        latestEvent = e;
        EventEnded?.Invoke(e);
    }

    public void PublishMeter(MeterFrame frame)
    {
        latestMeter = frame;
        MeterPublished?.Invoke(frame);
    }
}
