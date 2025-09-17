using LinkerPlayer.Core;
using LinkerPlayer.Models;
using ManagedBass;
using ManagedBass.Wasapi;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace LinkerPlayer.Audio;

public class OutputDeviceManager
{
    private readonly AudioEngine _audioEngine;
    private readonly SettingsManager _settingsManager;
    private readonly ILogger<OutputDeviceManager> _logger;

    private readonly List<string> _directSoundDevices = new();
    private readonly List<string> _wasapiDevices = new();
    private bool _isInitialized;
    private string _currentDeviceName = "Default";

    public OutputDeviceManager(AudioEngine audioEngine, SettingsManager settingsManager, ILogger<OutputDeviceManager> logger)
    {
        _audioEngine = audioEngine;
        _settingsManager = settingsManager;
        _logger = logger;
    }

    public void InitializeOutputDevice()
    {
        if (_isInitialized)
        {
            //_logger.LogInformation("OutputDeviceManager: Already initialized, skipping");
            return;
        }

        try
        {
            _directSoundDevices.Clear();
            GetDirectSoundDevices();
            SetOutputDevice();
            _isInitialized = true;
            _logger.LogInformation("OutputDeviceManager: Initialization complete");
        }
        catch (Exception ex)
        {
            _logger.LogError($"OutputDeviceManager: Initialization failed: {ex.Message}");
            throw;
        }
    }

    public List<string> GetDirectSoundDevices()
    {
        if (_directSoundDevices.Count > 0)
            return _directSoundDevices;

        try
        {
            for (int i = 0; i < Bass.DeviceCount; i++) // Start at 1 to skip "No sound" (index 0)
            {
                try
                {
                    DeviceInfo device = Bass.GetDeviceInfo(i);

                    _logger.LogInformation($"Device {i}: {device.Name} - {device.Type}");

                    if (string.IsNullOrEmpty(device.Name) || !device.IsEnabled)
                    {
                        _logger.LogDebug($"GetOutputDevicesList: Stopped at index {i} (empty name or disabled)");
                        break;
                    }

                    _directSoundDevices.Add(device.Name);
                }
                catch (BassException ex)
                {
                    _logger.LogDebug($"GetOutputDevicesList: Invalid device at index {i}: {ex.Message}");
                    break;
                }
            }

            _logger.LogInformation($"GetOutputDevicesList: Found {_directSoundDevices.Count} enabled devices");
            return _directSoundDevices;
        }
        catch (Exception ex)
        {
            _logger.LogError($"GetOutputDevicesList: Failed: {ex.Message}");
            return _directSoundDevices;
        }
    }

    public List<string> GetWasapiDevices()
    {
        if(_wasapiDevices.Count > 0)
            return _wasapiDevices;

        try
        {
            int wasapiCount = 0;
            for (int i = 0; BassWasapi.GetDeviceInfo(i, out var wasapiDevice); i++)
            {
                // Log all devices for debugging purposes
                //_logger.LogDebug($"Found WASAPI Device {i}: '{wasapiDevice.Name}' (ID: {wasapiDevice.ID}) - " +
                //              $"Enabled: {wasapiDevice.IsEnabled}, Input: {wasapiDevice.IsInput}, Default: {wasapiDevice.IsDefault}");

                // Only add enabled output devices to the list
                if (!string.IsNullOrEmpty(wasapiDevice.Name) &&
                    wasapiDevice.IsEnabled &&
                    !wasapiDevice.IsInput)
                {
                    string deviceDisplayName = wasapiDevice.Name;

                    // Add default indicator
                    if (wasapiDevice.IsDefault)
                        deviceDisplayName += " (Default)";

                    _wasapiDevices.Add(deviceDisplayName);
                    wasapiCount++;
                    _logger.LogInformation($"Added WASAPI Device {i}: {deviceDisplayName}");
                }
            }

            _logger.LogInformation($"Found {wasapiCount} enabled WASAPI output devices");
            return _wasapiDevices;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error enumerating WASAPI devices: {ex.Message}");
            return _wasapiDevices;
        }
    }

    public void SetOutputDevice(string deviceName = "Default")
    {
        try
        {
            bool isWasapiDevice = deviceName.StartsWith("WASAPI: ");
            string actualDeviceName = isWasapiDevice ? deviceName.Substring(8) : deviceName;

            if (isWasapiDevice)
            {
                // For WASAPI devices, we don't change the device here
                // WASAPI device selection happens during WASAPI initialization
                _currentDeviceName = deviceName;
                _logger.LogInformation($"Selected WASAPI device: {actualDeviceName}");
            }
            else
            {
                // For DirectSound devices, use the existing logic
                List<string> availableDevices = GetDirectSoundDevices();
                if (availableDevices.Contains(deviceName))
                {
                    _audioEngine.ReselectOutputDevice(deviceName);
                    _currentDeviceName = deviceName;
                    _logger.LogInformation($"DirectSound device set to: {deviceName}");
                }
                else
                {
                    _logger.LogWarning($"SetOutputDevice: Device '{deviceName}' not found, using default");
                    _audioEngine.ReselectOutputDevice("Default");
                    _currentDeviceName = "Default";
                }
            }

            _settingsManager.Settings.SelectedOutputDevice = deviceName;
            _settingsManager.SaveSettings(nameof(AppSettings.SelectedOutputDevice));
        }
        catch (Exception ex)
        {
            _logger.LogError($"SetOutputDevice: Failed: {ex.Message}");
        }
    }

    public string GetCurrentDeviceName()
    {
        if (!_isInitialized || !_audioEngine.IsInitialized)
        {
            _logger.LogDebug("GetCurrentDeviceName: OutputDeviceManager or BASS not initialized, returning cached name");
            return _currentDeviceName;
        }

        try
        {
            int currentDevice = Bass.CurrentDevice;
            DeviceInfo device = Bass.GetDeviceInfo(currentDevice);
            if (!string.IsNullOrEmpty(device.Name) && device.IsEnabled)
            {
                _currentDeviceName = device.Name;
                //_logger.LogDebug($"GetCurrentDeviceName: Returned {device.Name} (index {currentDevice})");
                return device.Name;
            }

            _logger.LogWarning("GetCurrentDeviceName: No valid device found, returning cached name");
            return _currentDeviceName;
        }
        catch (Exception ex)
        {
            _logger.LogError($"GetCurrentDeviceName: Failed: {ex.Message}");
            return _currentDeviceName;
        }
    }
}