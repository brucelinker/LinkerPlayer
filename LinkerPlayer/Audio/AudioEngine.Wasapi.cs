using LinkerPlayer.Models;
using ManagedBass;
using ManagedBass.Wasapi;
using Microsoft.Extensions.Logging;

namespace LinkerPlayer.Audio;

public partial class AudioEngine
{
    private int WasapiProc(IntPtr buffer, int length, IntPtr user)
    {
        if (_mixerStream == 0)
        {
            unsafe
            {
                byte* ptr = (byte*)buffer.ToPointer();
                for (int i = 0; i < length; i++)
                {
                    ptr[i] = 0;
                }
            }
            return length;
        }

        int bytesRead;
        if (_mixerIsFloat)
        {
            // Request float data
            bytesRead = Bass.ChannelGetData(_mixerStream, buffer, length | (int)DataFlags.Float);
        }
        else
        {
            // Request16-bit PCM data
            bytesRead = Bass.ChannelGetData(_mixerStream, buffer, length);
        }

        if (bytesRead > 0)
        {
            if (bytesRead < length)
            {
                unsafe
                {
                    byte* ptr = (byte*)buffer.ToPointer();
                    for (int i = bytesRead; i < length; i++)
                    {
                        ptr[i] = 0;
                    }
                }
            }
            return length;
        }
        else
        {
            unsafe
            {
                byte* ptr = (byte*)buffer.ToPointer();
                for (int i = 0; i < length; i++)
                {
                    ptr[i] = 0;
                }
            }
            return length;
        }
    }

    private bool InitializeWasapiForPlayback()
    {
        try
        {
            if (_audioDeviceLost)
            {
                _logger.LogInformation("Recovering from audio device loss - resetting error tracking");
                _audioDeviceLost = false;
                _consecutiveAudioErrors = 0;
                _lastAudioError = Errors.OK;
            }

            if (_wasapiInitialized && BassWasapi.IsStarted)
            {
                _logger.LogInformation("WASAPI already initialized and running - skipping re-init");
                return true;
            }

            _logger.LogInformation("Freeing any existing WASAPI state before re-init");
            BassWasapi.Stop();
            BassWasapi.Free();
            _wasapiInitialized = false;

            if (!BassWasapi.GetDeviceInfo(_currentDevice.Index, out WasapiDeviceInfo deviceInfo))
            {
                _logger.LogError($"Failed to get WASAPI device info for device {_currentDevice.Index}: {Bass.LastError}");
                return false;
            }

            bool exclusive = (_currentMode == OutputMode.WasapiExclusive);
            WasapiInitFlags baseFlags = exclusive ? WasapiInitFlags.Exclusive : WasapiInitFlags.Shared;
            WasapiInitFlags bufferedFlags = baseFlags | WasapiInitFlags.Buffer;
            const WasapiInitFlags FLOAT_FLAG = (WasapiInitFlags)0x100;

            float bufferLength = 0.10f;
            float period = 0f;

            bool success = false;

            if (_mixerIsFloat)
            {
                _logger.LogInformation($"Initializing WASAPI (buffered): Device={deviceInfo.Name}, Freq={deviceInfo.MixFrequency}, Channels={deviceInfo.MixChannels}, Mode={_currentMode}, Float=TRUE");
                success = BassWasapi.Init(_currentDevice.Index, deviceInfo.MixFrequency, deviceInfo.MixChannels, bufferedFlags | FLOAT_FLAG, bufferLength, period, _wasapiProc, IntPtr.Zero);
            }

            if (!success && _mixerIsFloat)
            {
                _logger.LogWarning($"WASAPI float buffered init failed: {Bass.LastError} - trying float without buffer");
                success = BassWasapi.Init(_currentDevice.Index, deviceInfo.MixFrequency, deviceInfo.MixChannels, baseFlags | FLOAT_FLAG, 0, 0, _wasapiProc, IntPtr.Zero);
            }

            if (!success)
            {
                _logger.LogInformation("Trying PCM buffered WASAPI init");
                success = BassWasapi.Init(_currentDevice.Index, deviceInfo.MixFrequency, deviceInfo.MixChannels, bufferedFlags, bufferLength, period, _wasapiProc, IntPtr.Zero);
            }

            if (!success)
            {
                _logger.LogWarning($"WASAPI PCM buffered init failed: {Bass.LastError} - trying PCM without buffer");
                success = BassWasapi.Init(_currentDevice.Index, deviceInfo.MixFrequency, deviceInfo.MixChannels, baseFlags, 0, 0, _wasapiProc, IntPtr.Zero);
            }

            if (success)
            {
                _wasapiInitialized = true;
                _logger.LogInformation($"WASAPI initialized successfully. Format={(_mixerIsFloat ? "float" : "16-bit PCM")}");
                return true;
            }
            else
            {
                _logger.LogError($"Failed to initialize WASAPI: {Bass.LastError}");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during WASAPI initialization");
            return false;
        }
    }

    private bool StartWasapiPlayback()
    {
        _logger.LogInformation("Starting WASAPI playback");
        if (!_wasapiInitialized)
        {
            if (!InitializeWasapiForPlayback())
            {
                _logger.LogError("Failed to initialize WASAPI for playback");
                if (Bass.LastError == Errors.Busy || Bass.LastError == Errors.Already)
                {
                    MarkDeviceBusyAndNotify("Playback cannot start.");
                }
                else
                {
                    _audioDeviceLost = true;
                }
                return false;
            }
        }

        if (_mixerStream != 0)
        {
            _logger.LogInformation("Calling Bass.ChannelPlay on mixer stream (handle {MixerStream})", _mixerStream);
            Bass.ChannelPlay(_mixerStream, false);
        }

        bool started = BassWasapi.Start();
        if (!started)
        {
            _logger.LogError($"BassWasapi.Start failed: {Bass.LastError}");
            if (Bass.LastError == Errors.Busy)
            {
                MarkDeviceBusyAndNotify("Playback cannot start.");
            }
            else
            {
                _audioDeviceLost = true;
            }
            return false;
        }
        _logger.LogInformation("BassWasapi.Start succeeded");
        return true;
    }

    private void PauseWasapi()
    {
        BassWasapi.Stop();
        if (_mixerStream != 0)
        {
            Bass.ChannelPause(_mixerStream);
        }
    }

    private bool ResumeWasapi()
    {
        if (_mixerStream != 0)
        {
            Bass.ChannelPlay(_mixerStream);
        }
        if (!BassWasapi.Start())
        {
            _logger.LogError($"Failed to resume WASAPI: {Bass.LastError}");
            return false;
        }
        return true;
    }

    private bool SeekWasapi(double position)
    {
        if (_decodeStream == 0)
        {
            _logger.LogError("Cannot seek: decode stream is invalid");
            return false;
        }

        long bytePosition = Bass.ChannelSeconds2Bytes(_decodeStream, position);
        if (bytePosition < 0)
        {
            _logger.LogError($"Failed to convert position {position} to bytes: {Bass.LastError}");
            return false;
        }

        if (!Bass.ChannelSetPosition(_decodeStream, bytePosition))
        {
            _logger.LogError($"Failed to seek to position {position}: {Bass.LastError}");
            return false;
        }

        double actualPosition = Bass.ChannelBytes2Seconds(_decodeStream, Bass.ChannelGetPosition(_decodeStream));
        if (!double.IsNaN(actualPosition) && actualPosition >= 0)
        {
            CurrentTrackPosition = actualPosition;
        }
        return true;
    }
}
