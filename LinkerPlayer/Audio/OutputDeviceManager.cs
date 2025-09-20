using LinkerPlayer.Core;
using LinkerPlayer.Models;
using ManagedBass;
using ManagedBass.Wasapi;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LinkerPlayer.Audio;

public enum DeviceType { DirectSound, Wasapi }

public record Device(string Name, DeviceType Type, int Index, bool IsDefault = false);

public class OutputDeviceManager : IDisposable
{
    private readonly AudioEngine _audioEngine;
    private readonly SettingsManager _settingsManager;
    private readonly ILogger<OutputDeviceManager> _logger;

    private readonly List<Device> _devices = new();
    private bool _isInitialized;
    private Device _currentDevice;

    public OutputDeviceManager(AudioEngine audioEngine, SettingsManager settingsManager, ILogger<OutputDeviceManager> logger)
    {
        _audioEngine = audioEngine;
        _settingsManager = settingsManager;
        _logger = logger;
        _currentDevice = new Device("Default", DeviceType.DirectSound, -1, true);
    }

    public void InitializeOutputDevice()
    {
        if (_isInitialized)
        {
            return;
        }

        try
        {
            RefreshDeviceList();
            SetOutputDevice(_settingsManager.Settings.SelectedOutputDevice ?? "Default");
            _isInitialized = true;
            _logger.LogInformation("OutputDeviceManager: Initialization complete");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OutputDeviceManager: Initialization failed");
            throw;
        }
    }

    private void RefreshDeviceList()
    {
        _devices.Clear();

        // Get DirectSound devices
        try
        {
            for (int i = 1; i < Bass.DeviceCount; i++) // Start from 1 to skip "Default"
            {
                var dsDevice = Bass.GetDeviceInfo(i);
                if (string.IsNullOrEmpty(dsDevice.Name) || !dsDevice.IsEnabled)
                    continue;

                _devices.Add(new Device(dsDevice.Name, DeviceType.DirectSound, i));
            }
            _logger.LogInformation("Found {Count} DirectSound devices", _devices.Count(d => d.Type == DeviceType.DirectSound));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enumerating DirectSound devices");
        }

        // Get WASAPI devices
        try
        {
            int wasapiCount = 0;
            for (int i = 0; BassWasapi.GetDeviceInfo(i, out var wasapiDevice); i++)
            {
                if (wasapiDevice.IsEnabled && !wasapiDevice.IsInput && !string.IsNullOrEmpty(wasapiDevice.Name))
                {
                    string deviceDisplayName = wasapiDevice.IsDefault ? $"{wasapiDevice.Name} (Default)" : wasapiDevice.Name;
                    _devices.Add(new Device(deviceDisplayName, DeviceType.Wasapi, i, wasapiDevice.IsDefault));
                    wasapiCount++;
                }
            }
            _logger.LogInformation("Found {Count} enabled WASAPI output devices", wasapiCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enumerating WASAPI devices");
        }
    }

    public IEnumerable<Device> GetOutputDevices()
    {
        if (!_audioEngine.IsBassInitialized)
        {
            _logger.LogWarning("GetOutputDevices: BASS not initialized");
            return Enumerable.Empty<Device>();
        }

        RefreshDeviceList();
        return _devices;
    }

    public void SetOutputDevice(string deviceName)
    {
        if (!_isInitialized)
        {
            RefreshDeviceList();
        }

        Device deviceToSet = _devices.FirstOrDefault(d => d.Name == deviceName)
                          ?? _devices.FirstOrDefault(d => d.IsDefault && d.Type == DeviceType.Wasapi)
                          ?? new Device("Default", DeviceType.DirectSound, -1, true);

        try
        {
            _audioEngine.ReselectOutputDevice(deviceToSet);
            _currentDevice = deviceToSet;
            _logger.LogInformation("Output device set to: {DeviceName}", deviceToSet.Name);

            _settingsManager.Settings.SelectedOutputDevice = deviceToSet.Name;
            _settingsManager.SaveSettings(nameof(AppSettings.SelectedOutputDevice));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SetOutputDevice failed for device '{DeviceName}'", deviceName);
        }
    }

    public Device GetCurrentDevice() => _currentDevice;

    public IEnumerable<Device> GetDirectSoundDevices()
    {
        if (!_audioEngine.IsBassInitialized)
        {
            _logger.LogWarning("GetDirectSoundDevices: BASS not initialized");
            return Enumerable.Empty<Device>();
        }
        RefreshDeviceList();
        return _devices.Where(d => d.Type == DeviceType.DirectSound);
    }

    public IEnumerable<Device> GetWasapiDevices()
    {
        if (!_audioEngine.IsBassInitialized)
        {
            _logger.LogWarning("GetWasapiDevices: BASS not initialized");
            return Enumerable.Empty<Device>();
        }
        RefreshDeviceList();
        return _devices.Where(d => d.Type == DeviceType.Wasapi);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _devices.Clear();
            _isInitialized = false;
            _logger.LogInformation("OutputDeviceManager: Disposed");
        }
    }
}