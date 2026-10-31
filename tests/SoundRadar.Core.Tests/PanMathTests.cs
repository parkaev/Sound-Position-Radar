using SoundRadar.Core.Dsp;
using Xunit;

namespace SoundRadar.Core.Tests;

public class PanMathTests
{
    [Theory]
    [InlineData(-90.0)]
    [InlineData(-45.0)]
    [InlineData(0.0)]
    [InlineData(45.0)]
    [InlineData(90.0)]
    public void Azimuth_RoundTripThroughConstantPowerPan(double azimuth)
    {
        var (gL, gR) = PanMath.ChannelGains(azimuth);
        var recovered = PanMath.AzimuthFromEnergies(gL * gL, gR * gR);
        Assert.InRange(recovered, azimuth - 0.5, azimuth + 0.5);
    }

    [Fact]
    public void Azimuth_FullyLeft_IsMinus90()
    {
        Assert.Equal(-90.0, PanMath.AzimuthFromEnergies(1.0, 0.0));
    }

    [Fact]
    public void Azimuth_FullyRight_IsPlus90()
    {
        Assert.Equal(90.0, PanMath.AzimuthFromEnergies(0.0, 1.0));
    }

    [Fact]
    public void Azimuth_Silence_IsCenter()
    {
        Assert.Equal(0.0, PanMath.AzimuthFromEnergies(0.0, 0.0));
    }
}
