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

        // Add our synthetic default DirectSound device entry once
        _devices.Add(new Device("Default", OutputDeviceType.DirectSound, -1, true));

        // Get DirectSound devices
        try
        {
            for (int i = 1; i < Bass.DeviceCount; i++) // Start from 1 to skip "No sound"
            {
                var dsDevice = Bass.GetDeviceInfo(i);
                _logger.LogDebug("DirectSound Device {Index}: Name='{Name}', IsEnabled={IsEnabled}, IsDefault={IsDefault}", i, dsDevice.Name, dsDevice.IsEnabled, dsDevice.IsDefault);
                if (string.IsNullOrEmpty(dsDevice.Name) || !dsDevice.IsEnabled)
                    continue;

                // Skip BASS's internal "Default" entry to avoid duplicates
                if (dsDevice.IsDefault || string.Equals(dsDevice.Name, "Default", StringComparison.OrdinalIgnoreCase))
                    continue;

                _devices.Add(new Device(dsDevice.Name, OutputDeviceType.DirectSound, i));
            }
            var dsCount = _devices.Count(d => d.Type == OutputDeviceType.DirectSound) - 1; // -1 for our synthetic default
            _logger.LogInformation("Found {Count} DirectSound devices (excluding synthetic Default)", dsCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enumerating DirectSound devices");
        }

        // Get WASAPI devices
        try
        {
            var wasapiList = new List<Device>();
            for (int i = 0; BassWasapi.GetDeviceInfo(i, out var wasapiDevice); i++) // WASAPI devices start from 0
            {
                _logger.LogDebug("WASAPI Device {Index}: Name='{Name}', IsEnabled={IsEnabled}, IsInput={IsInput}, IsDefault={IsDefault}", i, wasapiDevice.Name, wasapiDevice.IsEnabled, wasapiDevice.IsInput, wasapiDevice.IsDefault);
                if (wasapiDevice.IsEnabled && !wasapiDevice.IsInput && !string.IsNullOrEmpty(wasapiDevice.Name))
                {
                    // Keep the original device name; track default via IsDefault flag
                    wasapiList.Add(new Device(wasapiDevice.Name, OutputDeviceType.Wasapi, i, wasapiDevice.IsDefault));
                }
            }

            // Order WASAPI devices so that default Speakers (or any speakers) come first, headsets last
            var orderedWasapi = wasapiList
                .OrderBy(d => GetWasapiPriority(d.Name, d.IsDefault))
                .ThenBy(d => d.Name)
                .ToList();

            foreach (var dev in orderedWasapi)
            {
                _devices.Add(dev);
            }

            _logger.LogInformation("Found {Count} enabled WASAPI output devices (speaker-first order)", orderedWasapi.Count);
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

    // Heuristics to push speaker devices to the top and headset-style devices to the bottom
    private static int GetWasapiPriority(string name, bool isDefault)
    {
        var n = name?.ToLowerInvariant() ?? string.Empty;

        bool isSpeaker = n.Contains("speaker"); // matches "Speakers" as well
        bool isHeadset = n.Contains("headset") || n.Contains("headphone") || n.Contains("earphone") || n.Contains("hands-free") || n.Contains("earbud") || n.Contains("ear buds") || n.Contains("bt700");

        // Priority buckets (lower number = earlier in list)
        // 0: Default Speakers
        // 1: Any Speakers
        // 2: Other non-headset devices
        // 3: Headset-style devices
        if (isSpeaker && isDefault) return 0;
        if (isSpeaker) return 1;
        if (isHeadset) return 3;
        return 2;
    }
}