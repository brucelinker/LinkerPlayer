using System;
using System.Diagnostics;
using NAudio.Dsp;
using NAudio.Wave;

namespace LinkerPlayer.Audio;

public class SampleAggregator : ISampleProvider
{
    private float _maxValue;
    private float _minValue;
    public int NotificationCount { get; set; }
    int _count;

    // FFT
    public event EventHandler<FftEventArgs>? FftCalculated;
    public bool PerformFft { get; set; }
    private readonly Complex[] _fftBuffer;
    private readonly FftEventArgs _fftArgs;
    private int _fftPos;
    public const int FftLength = 2048;
    private readonly int _fftCalc;
    private readonly ISampleProvider _source;

    private readonly int _channels;

    public SampleAggregator(ISampleProvider source) // fftLength = 1024
    {
        _channels = source.WaveFormat.Channels;
        if (!IsPowerOfTwo(FftLength))
        {
            throw new ArgumentException("FFT Length must be a power of two");
        }
        this._fftCalc = (int)Math.Log(FftLength, 2.0);
        //this.FftLength = fftLength;
        this._fftBuffer = new Complex[FftLength];
        this._fftArgs = new FftEventArgs(_fftBuffer);
        this._source = source;
    }

    bool IsPowerOfTwo(int x)
    {
        return (x & (x - 1)) == 0;
    }


    //public int FftLength { get; }


    public void Reset()
    {
        _count = 0;
        _maxValue = _minValue = 0;
    }

    private void Add(float value)
    {
        if (PerformFft && FftCalculated != null)
        {
            _fftBuffer[_fftPos].X = (float)(value * FastFourierTransform.HammingWindow(_fftPos, FftLength));
            _fftBuffer[_fftPos].Y = 0;
            _fftPos++;
            if (_fftPos >= _fftBuffer.Length)
            {
                _fftPos = 0;
                // 1024 = 2^10
                FastFourierTransform.FFT(true, _fftCalc, _fftBuffer);
                FftCalculated(this, _fftArgs);
            }
        }

        _maxValue = Math.Max(_maxValue, value);
        _minValue = Math.Min(_minValue, value);
        _count++;
        if (_count >= NotificationCount && NotificationCount > 0)
        {
            Reset();
        }
    }

    /// <summary>
    /// Performs an FFT calculation on the channel data upon request.
    /// </summary>
    /// <param name="fftBuffer">A buffer where the FFT data will be stored.</param>

    int binaryExponentation = (int) Math.Log(FftLength, 2);

    public void CalcFftResults(float[] fftBuffer)
    {
        Complex[] channelDataClone = new Complex[FftLength];
        _fftBuffer.CopyTo(channelDataClone, 0);
        FastFourierTransform.FFT(true, binaryExponentation, channelDataClone);
        for (int i = 0; i < channelDataClone.Length / 2; i++)
        {
            // Calculate actual intensities for the FFT results.
            fftBuffer[i] = (float)Math.Sqrt(channelDataClone[i].X * channelDataClone[i].X + channelDataClone[i].Y * channelDataClone[i].Y);
        }
    }

    public WaveFormat WaveFormat { get { return _source.WaveFormat; } }

    public int Read(float[] buffer, int offset, int count)
    {
        int samplesRead = _source.Read(buffer, offset, count);

        for (int n = 0; n < samplesRead; n += _channels)
        {
            Add(buffer[n + offset]);
        }
        return samplesRead;
    }
}

public class FftEventArgs : EventArgs
{
    [DebuggerStepThrough]
    public FftEventArgs(Complex[] result)
    {
        this.Result = result;
    }
    public Complex[] Result { get; private set; }
}