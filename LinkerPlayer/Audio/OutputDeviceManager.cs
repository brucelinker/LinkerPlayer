using LinkerPlayer.Core;
using LinkerPlayer.Models;
using ManagedBass;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace LinkerPlayer.Audio;

public class OutputDeviceManager
{
    private readonly AudioEngine _audioEngine;
    private readonly SettingsManager _settingsManager;
    private readonly ILogger<OutputDeviceManager> _logger;

    private readonly List<string> Devices = new();
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
            _audioEngine.InitializeBass();
            Devices.Clear();
            GetOutputDevicesList();
            SetMainOutputDevice();
            _isInitialized = true;
            _logger.LogInformation("OutputDeviceManager: Initialization complete");
        }
        catch (Exception ex)
        {
            _logger.LogError($"OutputDeviceManager: Initialization failed: {ex.Message}");
            throw;
        }
    }

    public List<string> GetOutputDevicesList()
    {
        Devices.Clear();
        if (!_audioEngine.IsInitialized)
        {
            _logger.LogWarning("GetOutputDevicesList: BASS not initialized, initializing now");
            _audioEngine.InitializeBass();
        }

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

                    Devices.Add(device.Name);
                }
                catch (BassException ex)
                {
                    _logger.LogDebug($"GetOutputDevicesList: Invalid device at index {i}: {ex.Message}");
                    break;
                }
            }

            _logger.LogInformation($"GetOutputDevicesList: Found {Devices.Count} enabled devices");
            return Devices;
        }
        catch (Exception ex)
        {
            _logger.LogError($"GetOutputDevicesList: Failed: {ex.Message}");
            return Devices;
        }
    }

    public void SetMainOutputDevice(string deviceName = "Default")
    {
        try
        {
            if (Devices.Contains(deviceName))
            {
                _audioEngine.ReselectOutputDevice(deviceName);
                _currentDeviceName = deviceName;
                _logger.LogInformation($"MainOutputDevice: {deviceName}");
            }
            else
            {
                _logger.LogWarning($"SetMainOutputDevice: Device '{deviceName}' not found, using default");
                _audioEngine.ReselectOutputDevice("Default");
                _currentDeviceName = "Default";
                _logger.LogInformation("MainOutputDevice: Default");
            }

            _settingsManager.Settings.MainOutputDevice = deviceName;
            _settingsManager.SaveSettings(nameof(AppSettings.MainOutputDevice));
        }
        catch (Exception ex)
        {
            _logger.LogError($"SetMainOutputDevice: Failed: {ex.Message}");
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
                _logger.LogDebug($"GetCurrentDeviceName: Returned {device.Name} (index {currentDevice})");
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

    public void Dispose()
    {
        Devices.Clear();
        _isInitialized = false;
        _currentDeviceName = "Default";
        _logger.LogInformation("OutputDeviceManager: Disposed");
    }
}