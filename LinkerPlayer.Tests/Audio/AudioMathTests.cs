using LinkerPlayer.Audio;
using Shouldly;

namespace LinkerPlayer.Tests.Audio;

public class AudioMathTests
{
    [StaTheory]
    [InlineData(44100, 0, 2048, 0)]
    [InlineData(44100, 20, 2048, 0)] // very low freq -> first bin
    [InlineData(44100, 1000, 2048, 46)]
    [InlineData(48000, 1000, 2048, 42)]
    public void GetFftFrequencyIndex_ComputesExpectedBin(int sampleRate, int freq, int fftSize, int expected)
    {
        int idx = AudioMath.GetFftFrequencyIndex(sampleRate, freq, fftSize);
        idx.ShouldBe(expected);
    }

    [StaTheory]
    [InlineData(-20f, -12f)]
    [InlineData(0f, 0f)]
    [InlineData(6f, 6f)]
    [InlineData(20f, 12f)]
    public void ClampGain_ClampsToDefaults(float input, float expected)
    {
        float result = AudioMath.ClampGain(input);
        result.ShouldBe(expected, 0.001f);
    }
}
