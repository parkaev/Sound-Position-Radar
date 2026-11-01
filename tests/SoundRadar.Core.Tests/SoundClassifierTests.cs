using SoundRadar.Core.Dsp;
using SoundRadar.Core.Events;
using Xunit;

namespace SoundRadar.Core.Tests;

public class SoundClassifierTests
{
    [Fact]
    public void BassPeakShot_ClassifiesShot()
    {
        // CZ-550-подобный выстрел: пик в басах, верха рядом.
        var (type, conf) = SoundClassifier.Classify(new[] { 30.0, 20.0, 14.0, 12.0, 18.0 }, 1500);
        Assert.Equal(SoundEventType.Shot, type);
        Assert.True(conf is > 0.5 and <= 1.0);
    }

    [Fact]
    public void HfPeakShot_ClassifiesShot()
    {
        // AK-74-подобный выстрел: ствол и вч-щелчок в одном событии (реальная запись).
        var (type, _) = SoundClassifier.Classify(new[] { 24.2, 25.0, 24.9, 19.8, 12.0 }, 1500);
        Assert.Equal(SoundEventType.Shot, type);
    }

    [Fact]
    public void NearFlatLoudEvent_Shot()
    {
        // На границе порога маржи пик всё ещё зачитывается (реальный случай из датасета).
        var (type, _) = SoundClassifier.Classify(new[] { 25.5, 25.3, 21.2, 20.0, 21.1 }, 1500);
        Assert.Equal(SoundEventType.Shot, type);
    }

    [Fact]
    public void BulletCrack_ClassifiesClick()
    {
        // Отделившийся щелчок пули: пик только в верхних полосах, басы отстают на 8+ дБ.
        var (type, conf) = SoundClassifier.Classify(new[] { 12.0, 13.0, 19.0, 21.0, 22.0 }, 700);
        Assert.Equal(SoundEventType.Click, type);
        Assert.True(conf is > 0.5 and <= 1.0);
    }

    [Fact]
    public void Footsteps_ClassifiesFootsteps()
    {
        // Пик в середине (0.5–2к/2–6к), басы и верха отстают, событие короткое.
        var (type, conf) = SoundClassifier.Classify(new[] { 12.0, 10.0, 20.0, 19.0, 8.0 }, 600);
        Assert.Equal(SoundEventType.Footsteps, type);
        Assert.Equal(0.6, conf);
    }

    [Fact]
    public void FootstepsShape_LongDuration_Unknown()
    {
        var (type, _) = SoundClassifier.Classify(new[] { 12.0, 10.0, 20.0, 19.0, 8.0 }, 1500);
        Assert.Equal(SoundEventType.Unknown, type);
    }

    [Fact]
    public void QuietEvent_Unknown()
    {
        var (type, _) = SoundClassifier.Classify(new[] { 8.0, 7.0, 5.0, 4.0, 6.0 }, 500);
        Assert.Equal(SoundEventType.Unknown, type);
    }

    [Fact]
    public void EmptyProfile_Unknown()
    {
        var (type, _) = SoundClassifier.Classify(new double[5], 500);
        Assert.Equal(SoundEventType.Unknown, type);
    }
}
