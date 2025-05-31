using LinkerPlayer.Core;
using LinkerPlayer.Models;
using ManagedBass;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System;
using System.Collections.Generic;

namespace LinkerPlayer.Audio;

public class OutputDeviceManager
{
    private readonly AudioEngine _audioEngine;
    private readonly SettingsManager _settingsManager;

    private readonly List<string> Devices = new();
    private bool _isInitialized;
    private string _currentDeviceName = "Default";
    
    public OutputDeviceManager(AudioEngine audioEngine, SettingsManager settingsManager)
    {
        _audioEngine = audioEngine;
        _settingsManager = settingsManager;
    }

    public void InitializeOutputDevice()
    {
        if (_isInitialized)
        {
            Log.Information("OutputDeviceManager: Already initialized, skipping");
            return;
        }

        try
        {
            _audioEngine.Initialize();
            Devices.Clear();
            GetOutputDevicesList();
            SetMainOutputDevice();
            _isInitialized = true;
            Log.Information("OutputDeviceManager: Initialization complete");
        }
        catch (Exception ex)
        {
            Log.Error($"OutputDeviceManager: Initialization failed: {ex.Message}");
            throw;
        }
    }

    public List<string> GetOutputDevicesList()
    {
        Devices.Clear();
        if (!_audioEngine.IsInitialized)
        {
            Log.Warning("GetOutputDevicesList: BASS not initialized, initializing now");
            _audioEngine.Initialize();
        }

        try
        {
            for (int i = 0; i < Bass.DeviceCount; i++) // Start at 1 to skip "No sound" (index 0)
            {
                try
                {
                    DeviceInfo device = Bass.GetDeviceInfo(i);

                    Log.Information($"Device {i}: {device.Name} - {device.Type}");

                    if (string.IsNullOrEmpty(device.Name) || !device.IsEnabled)
                    {
                        Log.Debug($"GetOutputDevicesList: Stopped at index {i} (empty name or disabled)");
                        break;
                    }

                    Devices.Add(device.Name);
                }
                catch (BassException ex)
                {
                    Log.Debug($"GetOutputDevicesList: Invalid device at index {i}: {ex.Message}");
                    break;
                }
            }

            Log.Information($"GetOutputDevicesList: Found {Devices.Count} enabled devices");
            return Devices;
        }
        catch (Exception ex)
        {
            Log.Error($"GetOutputDevicesList: Failed: {ex.Message}");
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
                Log.Information($"MainOutputDevice: {deviceName}");
            }
            else
            {
                Log.Warning($"SetMainOutputDevice: Device '{deviceName}' not found, using default");
                _audioEngine.ReselectOutputDevice("Default");
                _currentDeviceName = "Default";
                Log.Information("MainOutputDevice: Default");
            }

            _settingsManager.Settings.MainOutputDevice = deviceName;
            _settingsManager.SaveSettings(nameof(AppSettings.MainOutputDevice));
        }
        catch (Exception ex)
        {
            Log.Error($"SetMainOutputDevice: Failed: {ex.Message}");
        }
    }

    public string GetCurrentDeviceName()
    {
        if (!_isInitialized || !_audioEngine.IsInitialized)
        {
            Log.Debug("GetCurrentDeviceName: OutputDeviceManager or BASS not initialized, returning cached name");
            return _currentDeviceName;
        }

        try
        {
            int currentDevice = Bass.CurrentDevice;
            DeviceInfo device = Bass.GetDeviceInfo(currentDevice);
            if (!string.IsNullOrEmpty(device.Name) && device.IsEnabled)
            {
                _currentDeviceName = device.Name;
                Log.Debug($"GetCurrentDeviceName: Returned {device.Name} (index {currentDevice})");
                return device.Name;
            }

            Log.Warning("GetCurrentDeviceName: No valid device found, returning cached name");
            return _currentDeviceName;
        }
        catch (Exception ex)
        {
            Log.Error($"GetCurrentDeviceName: Failed: {ex.Message}");
            return _currentDeviceName;
        }
    }

    public void Dispose()
    {
        Devices.Clear();
        _isInitialized = false;
        _currentDeviceName = "Default";
        Log.Information("OutputDeviceManager: Disposed");
    }
}