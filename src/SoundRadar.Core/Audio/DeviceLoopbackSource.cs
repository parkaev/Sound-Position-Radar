using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace SoundRadar.Core.Audio;

/// <summary>
/// Захват всего выходного устройства (WASAPI loopback). Фолбэк для старых Windows и отладки:
/// сюда попадает в том числе Discord/музыка, зато работает всегда.
/// </summary>
public sealed class DeviceLoopbackSource : IAudioSource
{
    private WasapiRecorder? recorder;
    private volatile bool running;

    public string Description => "Выходное устройство целиком (весь системный звук)";

    public WaveFormat? Format => recorder?.WaveFormat;

    public event Action<AudioChunk>? SamplesAvailable;

    public event Action<Exception?>? Stopped;

    public Task StartAsync(CancellationToken ct = default)
    {
        // Loopback-эндпоинт отдаёт mix format (обычно float, 2+ каналов) — конверсию делает движок.
        var rec = new WasapiRecorderBuilder()
            .WithLoopbackCapture()
            .WithBufferLength(20)
            .Build();

        rec.DataAvailable += OnDataAvailable;
        rec.RecordingStopped += OnRecordingStopped;
        running = true;
        rec.StartRecording();
        recorder = rec;
        return Task.CompletedTask;
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
