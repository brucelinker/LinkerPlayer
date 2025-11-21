namespace LinkerPlayer.Audio;

// Small, pure helpers to centralize audio-related math for reuse and unit testing
internal static class AudioMath
{
    public const int DefaultFftSize = 2048;

    // Maps a frequency to an FFT bin index based on sample rate and fft size
    public static int GetFftFrequencyIndex(int sampleRate, int frequency, int fftSize = DefaultFftSize)
    {
        if (frequency <= 0 || sampleRate <= 0 || fftSize <= 0)
        {
            return 0;
        }

        float binWidth = sampleRate / (float)fftSize;
        int index = (int)(frequency / binWidth);
        return Math.Clamp(index, 0, fftSize / 2 - 1);
    }

    // Clamp EQ gain to a safe range (in dB)
    public static float ClampGain(float gain, float min = -12f, float max = 12f)
    {
        return Math.Clamp(gain, min, max);
    }
}
