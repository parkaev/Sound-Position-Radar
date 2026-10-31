using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace SoundRadar.Core.Audio;

/// <summary>
/// Захват звука конкретного процесса (WASAPI process loopback, ActivateAudioInterfaceAsync).
/// Никаких инъекций и чтения памяти — ОС сама отдаёт микс процесса; тот же публичный механизм,
/// что у OBS «Application Audio Capture». Требуется Windows 10 build 19041+.
///
/// <paramref name="channels"/> = 8 запрашивает многоканальный микс (7.1): если игра рендерит
/// surround, мы видим зад ДО даунмикса в стерео. Если такой формат не активируется —
/// автоматический откат на стерео (см. <see cref="FellBackToStereo"/>).
/// </summary>
public sealed class ProcessLoopbackSource : IAudioSource
{
    /// <summary>На Win11 25H2 (26200+) есть регрессия: ActivateCompleted не приходит никогда.
    /// Таймаут даёт ясную ошибку вместо вечного зависания.</summary>
    private static readonly TimeSpan ActivationTimeout = TimeSpan.FromSeconds(8);

    private readonly int pid;
    private readonly bool includeTree;
    private readonly int channels;
    private WasapiRecorder? recorder;
    private volatile bool running;

    public ProcessLoopbackSource(int pid, bool includeTree = true, int channels = 2)
    {
        if (pid <= 0)
            throw new ArgumentOutOfRangeException(nameof(pid));
        if (channels is not (2 or 8))
            throw new ArgumentOutOfRangeException(nameof(channels), "Поддерживаются 2 или 8 каналов.");
        this.pid = pid;
        this.includeTree = includeTree;
        this.channels = channels;
    }

    /// <summary>true, если запросили 8 каналов, но система отдала только стерео.</summary>
    public bool FellBackToStereo { get; private set; }

    public string Description =>
        $"Процесс PID {pid}{(includeTree ? " (+ дочерние)" : "")}, {channels} кан.";
    public WaveFormat? Format => recorder?.WaveFormat;

    public event Action<AudioChunk>? SamplesAvailable;

    public event Action<Exception?>? Stopped;

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            throw new PlatformNotSupportedException(
                "Захват звука конкретного процесса требует Windows 10 build 19041 или новее.");

        try
        {
            recorder = await ActivateAsync(channels, ct).ConfigureAwait(false);
        }
        catch (Exception) when (channels == 8)
        {
            // Виртуальный loopback-девейс мог не принять 8-канальный формат — откат на стерео.
            FellBackToStereo = true;
            recorder = await ActivateAsync(2, ct).ConfigureAwait(false);
        }
        recorder.DataAvailable += OnDataAvailable;
        recorder.RecordingStopped += OnRecordingStopped;
        running = true;
        recorder.StartRecording();
    }

    private async Task<WasapiRecorder> ActivateAsync(int channelCount, CancellationToken ct)
    {
        // Активация только на MTA-потоке (thread pool) — на STA потоке активация может не завершиться.
        var build = Task.Run(() => new WasapiRecorderBuilder()
            .WithProcessLoopback((uint)pid,
                includeTree ? ProcessLoopbackMode.IncludeTargetProcessTree
                            : ProcessLoopbackMode.ExcludeTargetProcessTree)
            // Виртуальный loopback-девейс не отдаёт mix format — формат задаём явно.
            .WithFormat(WaveFormat.CreateIeeeFloatWaveFormat(48000, channelCount))
            .WithBufferLength(20)
            .BuildAsync());

        var finished = await Task.WhenAny(build, Task.Delay(ActivationTimeout, ct)).ConfigureAwait(false);
        if (finished != build)
            throw new TimeoutException(
                $"Активация process loopback не завершилась за {ActivationTimeout.TotalSeconds:F0} c. " +
                "Известная регрессия Windows 11 25H2 (build 26200+) — используйте захват всего устройства " +
                "или обновите Windows.");
        return await build.ConfigureAwait(false);
    }

    public Task StopAsync()
    {
        running = false;
        recorder?.StopRecording();
        return Task.CompletedTask;
    }

    private void OnDataAvailable(ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
    {
        if (!running || buffer.IsEmpty)
            return;

        var format = Format;
        if (format is null)
            return;

        var floats = new float[buffer.Length / sizeof(float)];
        MemoryMarshal.Cast<byte, float>(buffer).CopyTo(floats);
        SamplesAvailable?.Invoke(new AudioChunk(floats, format.Channels, format.SampleRate));
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        running = false;
        Stopped?.Invoke(e.Exception);
    }

    public async ValueTask DisposeAsync()
    {
        if (recorder is null)
            return;
        running = false;
        recorder.DataAvailable -= OnDataAvailable;
        recorder.RecordingStopped -= OnRecordingStopped;
        await recorder.DisposeAsync().ConfigureAwait(false);
        recorder = null;
    }
}
