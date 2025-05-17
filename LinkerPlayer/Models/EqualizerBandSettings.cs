namespace LinkerPlayer.Models;

public class EqualizerBandSettings
{
    public float Frequency { get; set; }
    public float Gain { get; set; }
    public float Bandwidth { get; set; }

    public EqualizerBandSettings(float frequency, float gain, float bandwidth)
    {
        Frequency = frequency;
        Gain = gain;
        Bandwidth = bandwidth;
    }
}
