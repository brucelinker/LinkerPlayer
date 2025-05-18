using ManagedBass;
using Serilog;
using System;
using System.Collections.Generic;

namespace LinkerPlayer.Audio;

public static class OutputDeviceManager
{
    private static readonly List<string> Devices = new();
    private static bool _isInitialized;
    private static string _currentDeviceName = "Default";

    public static void InitializeOutputDevice()
    {
        if (_isInitialized)
        {
            Log.Information("OutputDeviceManager: Already initialized, skipping");
            return;
        }

        try
        {
            AudioEngine.Initialize();
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

    public static List<string> GetOutputDevicesList()
    {
        Devices.Clear();
        if (!AudioEngine.IsInitialized)
        {
            Log.Warning("GetOutputDevicesList: BASS not initialized, initializing now");
            AudioEngine.Initialize();
        }

        try
        {
            // Use a reasonable max to avoid infinite loops
            for (int i = 1; i < 100; i++) // Start at 1 to skip "No sound" (index 0)
            {
                try
                {
                    DeviceInfo device = Bass.GetDeviceInfo(i);
                    if (string.IsNullOrEmpty(device.Name) || !device.IsEnabled)
                    {
                        Log.Debug($"GetOutputDevicesList: Stopped at index {i} (empty name or disabled)");
                        break;
                    }

                    Devices.Add(device.Name);
                    Log.Information($"Added device to list: {device.Name} (index {i})");
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

    public static void SetMainOutputDevice(string deviceName = "Default")
    {
        try
        {
            if (Devices.Contains(deviceName))
            {
                AudioEngine.Instance.ReselectOutputDevice(deviceName);
                _currentDeviceName = deviceName;
                Log.Information($"MainOutputDevice: {deviceName}");
            }
            else
            {
                Log.Warning($"SetMainOutputDevice: Device '{deviceName}' not found, using default");
                AudioEngine.Instance.ReselectOutputDevice("Default");
                _currentDeviceName = "Default";
                Log.Information("MainOutputDevice: Default");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"SetMainOutputDevice: Failed: {ex.Message}");
        }
    }

    public static string GetCurrentDeviceName()
    {
        if (!_isInitialized || !AudioEngine.IsInitialized)
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

    public static void Dispose()
    {
        Devices.Clear();
        _isInitialized = false;
        _currentDeviceName = "Default";
        Log.Information("OutputDeviceManager: Disposed");
    }
}