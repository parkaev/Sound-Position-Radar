namespace SoundRadar.Core.Audio;

/// <summary>
/// Чанк захваченного звука: interleaved float32, произвольное число каналов.
/// Выделяется новый массив на каждый чанк (~20 мс) — для MVP приемлемо, поток анализа
/// обрабатывает чанк синхронно в колбэке захвата.
/// </summary>
public readonly record struct AudioChunk(float[] Samples, int Channels, int SampleRate);

/// <summary>
/// Источник звука для анализатора. Контракт: отдаёт interleaved float32 любого числа каналов
/// (анализатор сам сводит к стерео). Реализации: <see cref="ProcessLoopbackSource"/> (основной)
/// и <see cref="DeviceLoopbackSource"/> (фолбэк).
/// </summary>
public interface IAudioSource : IAsyncDisposable
{
    string Description { get; }

    /// <summary>Формат захваченного потока (заполняется после успешного старта).</summary>
    NAudio.Wave.WaveFormat? Format { get; }

    /// <summary>Вызывается на потоке захвата. Обработчик должен быть быстрым.</summary>
    event Action<AudioChunk>? SamplesAvailable;

    /// <summary>Захват остановился (сам или по ошибке; исключение — если с ошибкой).</summary>
    event Action<Exception?>? Stopped;

    Task StartAsync(CancellationToken ct = default);

    Task StopAsync();
}
