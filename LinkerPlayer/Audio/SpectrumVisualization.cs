using LinkerPlayer.UserControls;

namespace LinkerPlayer.Audio;

class SpectrumVisualization //: IVisualization
{
    private readonly SpectrumAnalyzer _spectrumAnalyzer = new();

    public string Name => "Spectrum Analyzer";

    public object Content => _spectrumAnalyzer;

    public void OnMaxCalculated(float min, float max)
    {
        // nothing to do
    }

    //public void OnFftCalculated(NAudio.Dsp.Complex[] result)
    //{
    //    _spectrumAnalyzer.Update(result);
    //}
}