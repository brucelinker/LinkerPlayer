using LinkerPlayer.Audio;
using NAudio.Dsp;
using NAudio.Extras;
using System;
using System.Windows;
using System.Windows.Controls;

namespace LinkerPlayer.UserControls;

/// <summary>
/// Interaction logic for SpectrumAnalyzer.xaml
/// </summary>
public partial class SpectrumAnalyzerNAudio : UserControl
{
    private double xScale = 200;
    //private int bins = 512; // guess a 1024 size FFT, bins is half FFT size
    private int bins = 2048; // guess a 4096 size FFT, bins is half FFT size

    public SpectrumAnalyzerNAudio()
    {
        InitializeComponent();
        CalculateXScale();
        SizeChanged += SpectrumAnalyzer_SizeChanged;


        AudioEngine.MaximumCalculated += audioEngine_MaximumCalculated;
        AudioEngine.FftCalculated += audioEngine_FftCalculated;
    }

    private void audioEngine_FftCalculated(object? sender, FftEventArgs e)
    {
        Update(e.Result);
    }

    private void audioEngine_MaximumCalculated(object? sender, MaxSampleEventArgs e)
    {
        
    }

    void SpectrumAnalyzer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        CalculateXScale();
    }

    private void CalculateXScale()
    {
        xScale = ActualWidth / (bins/BinsPerPoint);
    }

    private const int BinsPerPoint = 4; // reduce the number of points we plot for a less jagged line?
    private int _updateCount;

    public void Update(Complex[] fftResults)
    {
        // no need to repaint too many frames per second
        if (_updateCount++ % 2 == 0)
        {
            return;
        }

        if (fftResults.Length / 2 != bins)
        {
            bins = fftResults.Length / 2;
            CalculateXScale();
        }
            
        for (int n = 0; n < fftResults.Length / 2; n+= BinsPerPoint)
        {
            // averaging out bins
            double yPos = 0;
            for (int b = 0; b < BinsPerPoint; b++)
            {
                yPos += GetYPosLog(fftResults[n+b]);
            }
            AddResult(n / BinsPerPoint, yPos / BinsPerPoint);
        }
    }

    private double GetYPosLog(Complex c)
    {
        // not entirely sure whether the multiplier should be 10 or 20 in this case.
        // going with 10 from here http://stackoverflow.com/a/10636698/7532
        double intensityDB = 10 * Math.Log10(Math.Sqrt(c.X * c.X + c.Y * c.Y));
        double minDB = -90;
        if (intensityDB < minDB) intensityDB = minDB;
        double percent = intensityDB / minDB;
        // we want 0dB to be at the top (i.e. yPos = 0)
        double yPos = percent * ActualHeight;
        return yPos;
    }

    private void AddResult(int index, double power)
    {
        Point p = new Point(CalculateXPos(index), power);
        if (index >= polyline1.Points.Count)
        {
            polyline1.Points.Add(p);
        }
        else
        {
            polyline1.Points[index] = p;
        }
    }

    private double CalculateXPos(int bin)
    {
        if (bin == 0) return 0;
        return bin * xScale; // Math.Log10(bin) * xScale;
    }
}