using NAudio.Wave;
using Serilog;
using System;

namespace LinkerPlayer.Audio;

public class AudioStream
{
    protected WaveOutEvent? OutputDevice;
    public event EventHandler<EventArgs>? StoppedEvent;

    public AudioStream(string deviceName)
    {
        SelectOutputDevice(deviceName);
    }

    public float OutputDeviceVolume
    {
        get => OutputDevice?.Volume ?? 0f;
        set
        {
            if (value is < 0f or > 1f)
            {
                Log.Error("Volume < 0.0 or > 1.0");

                if (value < 0)
                {
                    if (OutputDevice != null) OutputDevice.Volume = 0f;
                }
                else
                {
                    if (OutputDevice != null) OutputDevice.Volume = 1f;
                }
            }
            else
            {
                if (OutputDevice != null) OutputDevice.Volume = value;
            }
        }
    }

    public void SelectOutputDevice(string deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            Log.Error("Device name can`t be null");

            throw new ArgumentNullException(nameof(deviceName));
        }
        else
        {
            OutputDevice = new WaveOutEvent()
            {
                DeviceNumber = Audio.OutputDevice.GetOutputDeviceId(deviceName)
            };

            OutputDevice.PlaybackStopped += PlaybackStopped;

            Log.Information("Device has been selected");
        }
    }

    public virtual void PlaybackStopped(object? sender, EventArgs e)
    {
        if (StoppedEvent != null)
        {
            try
            {
                float unused = OutputDeviceVolume;
            }
            catch (Exception ex)
            {
                Log.Error(ex.Message);
                if (ex.Message == "NoDriver calling waveOutGetVolume")
                {
                    sender = null;
                    SelectOutputDevice(Audio.OutputDevice.GetOutputDeviceNameById(0));
                }
            }

            StoppedEvent(sender, e);
        }
    }

    public virtual void Play()
    {
        if (OutputDevice != null)
        {
            string outputDeviceName = Audio.OutputDevice.GetOutputDeviceNameById(OutputDevice.DeviceNumber);
            Log.Information($"Playing to {outputDeviceName}.");

            OutputDevice?.Play();
        }
    }

    public virtual void Stop()
    {
        if (OutputDevice != null)
        {
            string outputDeviceName = Audio.OutputDevice.GetOutputDeviceNameById(OutputDevice.DeviceNumber);
            Log.Information($"Stopped {outputDeviceName}.");

            OutputDevice?.Stop();
        }
    }

    public virtual void Pause()
    {
        if (OutputDevice != null)
        {
            string outputDeviceName = Audio.OutputDevice.GetOutputDeviceNameById(OutputDevice.DeviceNumber);
            Log.Information($"Paused {outputDeviceName}.");

            OutputDevice?.Pause();
        }
    }

    public virtual void CloseStream()
    {
        Log.Information("Stream was closed");
        OutputDevice?.Dispose();
    }

    public bool IsPlaying => OutputDevice is { PlaybackState: PlaybackState.Playing };

    public bool IsPaused => OutputDevice is { PlaybackState: PlaybackState.Paused };

    public bool IsStopped => OutputDevice is { PlaybackState: PlaybackState.Stopped };

    public int GetOutputDeviceId()
    {
        if (OutputDevice != null) return OutputDevice.DeviceNumber;

        return -1;
    }
}