using LinkerPlayer.Models;
using ManagedBass;
using ManagedBass.Wasapi;
using Microsoft.Extensions.Logging;

namespace LinkerPlayer.Audio;

public partial class AudioEngine
{
    private readonly float[] _fftBuffer = new float[2048];

    private int _positionTelemetryTick;

    public float[] FftUpdate
    {
        get; private set;
    }
    public double NoiseFloorDb { get; set; } = -60;
    public int ExpectedFftSize => 2048;

    public double GetDecibelLevel()
    {
        int level;
        if (_currentMode == OutputMode.DirectSound)
        {
            level = Bass.ChannelGetLevel(CurrentStream);
            if (level == -1 && Bass.LastError == Errors.Busy)
            {
                CheckAudioDeviceLost();
                return double.NaN;
            }
        }
        else
        {
            if (_audioDeviceLost)
            {
                return double.NaN;
            }
            level = BassWasapi.GetLevel();
            if (level == -1 && Bass.LastError == Errors.Busy)
            {
                CheckAudioDeviceLost();
                return double.NaN;
            }
            if (CheckAudioDeviceLost())
            {
                return double.NaN;
            }
        }
        if (level == -1)
        {
            return double.NaN;
        }
        int left = level & 0xFFFF;
        int right = (level >> 16) & 0xFFFF;
        double leftDb = 20 * Math.Log10(left / 32768.0);
        double rightDb = 20 * Math.Log10(right / 32768.0);
        double avgDb = (leftDb + rightDb) / 2.0;
        return avgDb;
    }

    public (double LeftDb, double RightDb) GetStereoDecibelLevels()
    {
        int level;
        if (_currentMode == OutputMode.DirectSound)
        {
            level = Bass.ChannelGetLevel(CurrentStream);
            if (level == -1 && Bass.LastError == Errors.Busy)
            {
                CheckAudioDeviceLost();
                return (double.NaN, double.NaN);
            }
        }
        else
        {
            if (_audioDeviceLost)
            {
                return (double.NaN, double.NaN);
            }
            level = BassWasapi.GetLevel();
            if (level == -1 && Bass.LastError == Errors.Busy)
            {
                CheckAudioDeviceLost();
                return (double.NaN, double.NaN);
            }
            if (CheckAudioDeviceLost())
            {
                return (double.NaN, double.NaN);
            }
        }
        if (level == -1)
        {
            return (double.NaN, double.NaN);
        }
        int left = level & 0xFFFF;
        int right = (level >> 16) & 0xFFFF;
        double leftDb = left > 0 ? 20 * Math.Log10(left / 32768.0) : -120.0;
        double rightDb = right > 0 ? 20 * Math.Log10(right / 32768.0) : -120.0;
        return (leftDb, rightDb);
    }

    public int GetFftFrequencyIndex(int frequency)
    {
        const int fftSize = 2048;
        int rate = _sampleRate > 0 ? _sampleRate : 44100;
        return AudioMath.GetFftFrequencyIndex(rate, frequency, fftSize);
    }

    public bool GetFftData(float[] fftDataBuffer)
    {
        if (fftDataBuffer.Length != ExpectedFftSize)
        {
            return false;
        }

        if (CurrentStream == 0)
        {
            Array.Clear(fftDataBuffer, 0, fftDataBuffer.Length);
            return false;
        }

        int bytesRead;
        if (_currentMode == OutputMode.DirectSound)
        {
            bytesRead = Bass.ChannelGetData(CurrentStream, fftDataBuffer, (int)DataFlags.FFT2048);
            if (bytesRead <= 0 && Bass.LastError == Errors.Busy)
            {
                CheckAudioDeviceLost();
                Array.Clear(fftDataBuffer, 0, fftDataBuffer.Length);
                return false;
            }
        }
        else
        {
            if (_audioDeviceLost)
            {
                Array.Clear(fftDataBuffer, 0, fftDataBuffer.Length);
                return false;
            }
            bytesRead = BassWasapi.GetData(fftDataBuffer, (int)DataFlags.FFT2048);
            if (bytesRead <= 0 && Bass.LastError == Errors.Busy)
            {
                CheckAudioDeviceLost();
                Array.Clear(fftDataBuffer, 0, fftDataBuffer.Length);
                return false;
            }
            if (CheckAudioDeviceLost())
            {
                Array.Clear(fftDataBuffer, 0, fftDataBuffer.Length);
                return false;
            }
        }

        if (bytesRead <= 0)
        {
            Array.Clear(fftDataBuffer, 0, fftDataBuffer.Length);
            return false;
        }

        return true;
    }

    private void HandleFftCalculated()
    {
        if (_audioDeviceLost)
        {
            return;
        }

        if (_currentMode == OutputMode.DirectSound && IsPlaying && CurrentStream != 0)
        {
            PlaybackState state = Bass.ChannelIsActive(CurrentStream);
            if (state == PlaybackState.Stopped)
            {
                _audioDeviceLost = true;
                _logger.LogError("DirectSound playback has stopped unexpectedly - device taken by exclusive app!");
                Stop();
                MarkDeviceBusyAndNotify("Playback has been stopped.");
                return;
            }
        }

        if (_currentMode != OutputMode.DirectSound && IsPlaying && _wasapiInitialized)
        {
            if (!BassWasapi.IsStarted)
            {
                _audioDeviceLost = true;
                _logger.LogError("WASAPI playback has stopped unexpectedly - device taken by exclusive app!");
                Stop();
                MarkDeviceBusyAndNotify("Playback has been stopped.");
                return;
            }
        }

        if (CurrentStream != 0)
        {
            PlaybackState state = Bass.ChannelIsActive(CurrentStream);
            if (CheckAudioDeviceLost())
            {
                return;
            }

            if (state != PlaybackState.Playing)
            {
                return;
            }

            // Update track position for UI/coordinator.
            try
            {
                if (_currentMode == OutputMode.DirectSound)
                {
                    int positionStream = _decodeStream != 0 ? _decodeStream : CurrentStream;
                    long posBytes = Bass.ChannelGetPosition(positionStream);
                    double posSeconds = Bass.ChannelBytes2Seconds(positionStream, posBytes);
                    if (!double.IsNaN(posSeconds) && posSeconds >= 0)
                    {
                        CurrentTrackPosition = posSeconds;
                    }

                    int tick = System.Threading.Interlocked.Increment(ref _positionTelemetryTick);
                    if ((tick % 50) == 0)
                    {
                        _logger.LogDebug("PositionTelemetry DS (Tick={Tick}, CurrentStream={CurrentStream}, DecodeStream={DecodeStream}, PositionStream={PositionStream}, PosBytes={PosBytes}, PosSeconds={PosSeconds}, LenSeconds={LenSeconds}, IsPlaying={IsPlaying}, State={State})",
                            tick,
                            CurrentStream,
                            _decodeStream,
                            positionStream,
                            posBytes,
                            posSeconds,
                            CurrentTrackLength,
                            IsPlaying,
                            state);
                    }
                }
                else
                {
                    int positionStream = _decodeStream != 0 ? _decodeStream : CurrentStream;
                    long posBytes = Bass.ChannelGetPosition(positionStream);
                    double posSeconds = Bass.ChannelBytes2Seconds(positionStream, posBytes);
                    if (!double.IsNaN(posSeconds) && posSeconds >= 0)
                    {
                        CurrentTrackPosition = posSeconds;
                    }

                    int tick = System.Threading.Interlocked.Increment(ref _positionTelemetryTick);
                    if ((tick % 25) == 0)
                    {
                        PlaybackState decodeState = _decodeStream != 0 ? Bass.ChannelIsActive(_decodeStream) : PlaybackState.Stopped;
                        PlaybackState mixerState = _mixerStream != 0 ? Bass.ChannelIsActive(_mixerStream) : PlaybackState.Stopped;

                        //_logger.LogDebug("PositionTelemetry WASAPI (Tick={Tick}, CurrentStream={CurrentStream}, DecodeStream={DecodeStream}, MixerStream={MixerStream}, PositionStream={PositionStream}, PosBytes={PosBytes}, PosSeconds={PosSeconds}, LenSeconds={LenSeconds}, WasapiStarted={WasapiStarted}, IsPlaying={IsPlaying}, CurrentState={CurrentState}, DecodeState={DecodeState}, MixerState={MixerState}, LoadedPath={LoadedPath})",
                        //    tick,
                        //    CurrentStream,
                        //    _decodeStream,
                        //    _mixerStream,
                        //    positionStream,
                        //    posBytes,
                        //    posSeconds,
                        //    CurrentTrackLength,
                        //    BassWasapi.IsStarted,
                        //    IsPlaying,
                        //    state,
                        //    decodeState,
                        //    mixerState,
                        //    LoadedTrackPath);
                    }

                    if (CurrentTrackLength > 0 && posSeconds >= 0)
                    {
                        double remainingSeconds = Math.Max(0, CurrentTrackLength - posSeconds);
                        if (remainingSeconds <= 1.0)
                        {
                            _logger.LogDebug("PositionTelemetry WASAPI near-EOF (CurrentStream={CurrentStream}, DecodeStream={DecodeStream}, MixerStream={MixerStream}, PositionStream={PositionStream}, PosSeconds={PosSeconds}, LenSeconds={LenSeconds}, RemainingSeconds={RemainingSeconds})",
                                CurrentStream,
                                _decodeStream,
                                _mixerStream,
                                positionStream,
                                posSeconds,
                                CurrentTrackLength,
                                remainingSeconds);
                        }
                    }
                }
            }
            catch
            {
            }
        }
        else
        {
            return;
        }

        int bytesRead;
        if (_currentMode == OutputMode.DirectSound)
        {
            bytesRead = Bass.ChannelGetData(CurrentStream, _fftBuffer, (int)DataFlags.FFT2048);
            if (bytesRead < 0 && Bass.LastError == Errors.Busy)
            {
                _logger.LogError("DirectSound ChannelGetData failed: BASS_ERROR_BUSY");
                CheckAudioDeviceLost();
                return;
            }
        }
        else
        {
            if (_audioDeviceLost)
            {
                return;
            }

            bytesRead = BassWasapi.GetData(_fftBuffer, (int)DataFlags.FFT2048);
            if (bytesRead < 0)
            {
                Errors error = Bass.LastError;
                if (error == Errors.Busy)
                {
                    _logger.LogError("WASAPI GetData failed: BASS_ERROR_BUSY - Device taken by exclusive app");
                    CheckAudioDeviceLost();
                    return;
                }
                else if (error != Errors.OK && error != Errors.Unknown)
                {
                    _logger.LogWarning("WASAPI GetData returned {BytesRead}, Bass.LastError = {Error}", bytesRead, error);
                }
            }
            if (CheckAudioDeviceLost())
            {
                return;
            }
        }

        int fftSize;
        if (bytesRead < 0)
        {
            fftSize = _fftBuffer.Length / 2;
            FftUpdate = new float[fftSize];
            Array.Clear(FftUpdate, 0, fftSize);
            OnFftCalculated?.Invoke(FftUpdate);
            return;
        }

        fftSize = _fftBuffer.Length / 2;
        float[] fftResult = new float[fftSize];
        for (int i = 0; i < fftSize; i++)
        {
            float real = _fftBuffer[i * 2];
            float imag = _fftBuffer[i * 2 + 1];
            float magnitude = (float)Math.Sqrt(real * real + imag * imag);
            float db = 20 * (float)Math.Log10(magnitude > 0 ? magnitude : 1e-5f);
            fftResult[i] = db < NoiseFloorDb ? 0f : Math.Max(0, (db + 120f) / 120f) * 0.5f;
        }

        const int barCount = 32;
        float[] barValues = new float[barCount];
        int binsPerBar = fftSize / barCount;
        for (int bar = 0; bar < barCount; bar++)
        {
            int startBin = bar * binsPerBar;
            int endBin = (bar == barCount - 1) ? fftSize : (bar + 1) * binsPerBar;
            float sum = 0f;
            int binCount = endBin - startBin;
            for (int bin = startBin; bin < endBin; bin++)
            {
                sum += fftResult[bin];
            }

            barValues[bar] = binCount > 0 ? sum / binCount : 0f;
        }

        for (int i = 0; i < fftSize; i++)
        {
            int barIndex = (int)((float)i / fftSize * barCount);
            barIndex = Math.Clamp(barIndex, 0, barCount - 1);
            fftResult[i] = barValues[barIndex];
        }
        FftUpdate = fftResult;
        OnFftCalculated?.Invoke(FftUpdate);
    }

    public void NextTrackPreStopVisuals()
    {
        try
        {
            int zeroLen = Math.Max(1, ExpectedFftSize / 2);
            OnFftCalculated?.Invoke(new float[zeroLen]);
        }
        catch { }
    }
}
