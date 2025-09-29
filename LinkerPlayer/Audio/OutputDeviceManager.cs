using LinkerPlayer.Models;
using ManagedBass;
using ManagedBass.Wasapi;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LinkerPlayer.Audio;

public interface IOutputDeviceManager
{
    IEnumerable<Device> RefreshOutputDeviceList();
    IEnumerable<Device> GetDirectSoundDevices();
    IEnumerable<Device> GetWasapiDevices();
}

public class OutputDeviceManager : IOutputDeviceManager, IDisposable
{
    private readonly ILogger<OutputDeviceManager> _logger;

    private readonly List<Device> _devices = new();

    public OutputDeviceManager(ILogger<OutputDeviceManager> logger)
    {
        _logger = logger;
    }

    public IEnumerable<Device> Devices => _devices;

    public IEnumerable<Device> RefreshOutputDeviceList()
    {
        _devices.Clear();

        // Add default DirectSound device
        _devices.Add(new Device("Default", OutputDeviceType.DirectSound, -1, true));

        // Get DirectSound devices
        try
        {
            for (int i = 1; i < Bass.DeviceCount; i++) // Start from 1 to skip "No sound"
            {
                var dsDevice = Bass.GetDeviceInfo(i);
                if (string.IsNullOrEmpty(dsDevice.Name) || !dsDevice.IsEnabled)
                    continue;

                _devices.Add(new Device(dsDevice.Name, OutputDeviceType.DirectSound, i));
            }
            _logger.LogInformation("Found {Count} DirectSound devices", _devices.Count(d => d.Type == OutputDeviceType.DirectSound) - 1); // -1 for default
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enumerating DirectSound devices");
        }

        // Get WASAPI devices
        try
        {
            int wasapiCount = 0;
            for (int i = 0; BassWasapi.GetDeviceInfo(i, out var wasapiDevice); i++) // WASAPI devices start from 0
            {
                if (wasapiDevice.IsEnabled && !wasapiDevice.IsInput && !string.IsNullOrEmpty(wasapiDevice.Name))
                {
                    string deviceDisplayName = wasapiDevice.IsDefault ? $"{wasapiDevice.Name} (Default)" : wasapiDevice.Name;
                    _devices.Add(new Device(deviceDisplayName, OutputDeviceType.Wasapi, i, wasapiDevice.IsDefault));
                    wasapiCount++;
                }
            }
            _logger.LogInformation("Found {Count} enabled WASAPI output devices", wasapiCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enumerating WASAPI devices");
        }

        return _devices;
    }

    public IEnumerable<Device> GetDirectSoundDevices()
    {
        if (_devices == null || !_devices.Any())
        {
            RefreshOutputDeviceList();
        }

        if(_devices == null)
            return Enumerable.Empty<Device>();

        return _devices.Where(d => d.Type == OutputDeviceType.DirectSound);
    }

    public IEnumerable<Device> GetWasapiDevices()
    {
        if (_devices == null || !_devices.Any())
        {
            RefreshOutputDeviceList();
        }

        if (_devices == null)
            return Enumerable.Empty<Device>();

        return _devices.Where(d => d.Type == OutputDeviceType.Wasapi);
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
            _logger.LogInformation("OutputDeviceManager: Disposed");
        }
    }
}