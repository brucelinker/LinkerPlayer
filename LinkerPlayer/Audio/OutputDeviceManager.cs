using LinkerPlayer.Models;
using ManagedBass;
using ManagedBass.Wasapi;
using Microsoft.Extensions.Logging;

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

    private const string PreferredDirectSoundDefaultToken = "USB";
    private const string PreferredWasapiDefaultToken = "Realtek";

    public OutputDeviceManager(ILogger<OutputDeviceManager> logger)
    {
        _logger = logger;
    }

    public IEnumerable<Device> Devices => _devices;

    public IEnumerable<Device> RefreshOutputDeviceList()
    {
        _devices.Clear();

        string primaryLabel = "Primary Sound Driver";
        try
        {
            for (int i = 1; i < Bass.DeviceCount; i++)
            {
                DeviceInfo info = Bass.GetDeviceInfo(i);
                if (info.IsEnabled && info.IsDefault && !string.IsNullOrWhiteSpace(info.Name))
                {
                    string resolved = NormalizeDeviceDisplayName(info.Name);
                    primaryLabel = $"Primary Sound Driver ({resolved})";
                    break;
                }
            }
        }
        catch
        {
        }

        // DirectSound: match common players
        _devices.Add(new Device(primaryLabel, OutputDeviceType.DirectSound, -1, true));

        // Get DirectSound devices
        try
        {
            List<Device> dsDevices = new List<Device>();
            for (int i = 1; i < Bass.DeviceCount; i++) // Start from 1 to skip "No sound"
            {
                DeviceInfo dsDevice = Bass.GetDeviceInfo(i);
                if (string.IsNullOrEmpty(dsDevice.Name) || !dsDevice.IsEnabled)
                {
                    continue;
                }

                if (dsDevice.IsDefault || string.Equals(dsDevice.Name, "Default", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string displayName = NormalizeDeviceDisplayName(dsDevice.Name);
                dsDevices.Add(new Device(displayName, OutputDeviceType.DirectSound, i));
            }

            dsDevices = DisambiguateDuplicateNames(dsDevices);

            List<Device> orderedDs = dsDevices
                .OrderByDescending(d => d.Name.Contains(PreferredDirectSoundDefaultToken, StringComparison.OrdinalIgnoreCase))
                .ThenBy(d => d.Name)
                .ToList();

            foreach (Device dev in orderedDs)
            {
                _devices.Add(dev);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enumerating DirectSound devices");
        }

        // Get WASAPI devices
        try
        {
            List<Device> wasapiDevices = new List<Device>();
            for (int i = 0; BassWasapi.GetDeviceInfo(i, out WasapiDeviceInfo wasapiDevice); i++)
            {
                if (wasapiDevice.IsEnabled && !wasapiDevice.IsInput && !string.IsNullOrEmpty(wasapiDevice.Name))
                {
                    string displayName = NormalizeDeviceDisplayName(wasapiDevice.Name);
                    wasapiDevices.Add(new Device(displayName, OutputDeviceType.Wasapi, i, wasapiDevice.IsDefault));
                }
            }

            wasapiDevices = DisambiguateDuplicateNames(wasapiDevices);

            List<Device> orderedWasapi = wasapiDevices
                .OrderByDescending(d => d.Name.Contains(PreferredWasapiDefaultToken, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(d => d.IsDefault)
                .ThenBy(d => d.Name)
                .ToList();

            foreach (Device dev in orderedWasapi)
            {
                _devices.Add(dev);
            }

            _logger.LogInformation("Found {Count} enabled WASAPI output devices", orderedWasapi.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enumerating WASAPI devices");
        }

        return _devices;
    }

    private static string NormalizeDeviceDisplayName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        // Keep WASAPI/Windows-friendly names as-is; only trim obvious trailing metadata.
        // Examples we want to keep: "Speakers (Realtek(R) Audio)", "Headset Earphone (Jabra ...)".
        // Examples we want to trim: trailing " - " driver-instance decorations.
        string trimmed = name.Trim();

        int dashIndex = trimmed.IndexOf(" - ", StringComparison.Ordinal);
        if (dashIndex > 0)
        {
            trimmed = trimmed.Substring(0, dashIndex).Trim();
        }

        return trimmed;
    }

    private static List<Device> DisambiguateDuplicateNames(List<Device> devices)
    {
        Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Device device in devices)
        {
            string key = device.Name;
            if (counts.TryGetValue(key, out int existing))
            {
                counts[key] = existing + 1;
            }
            else
            {
                counts[key] = 1;
            }
        }

        List<Device> result = new List<Device>(devices.Count);
        foreach (Device device in devices)
        {
            if (counts.TryGetValue(device.Name, out int count) && count > 1)
            {
                // Only disambiguate when needed.
                result.Add(device with { Name = $"{device.Name} [{device.Index}]" });
            }
            else
            {
                result.Add(device);
            }
        }

        return result;
    }

    public IEnumerable<Device> GetDirectSoundDevices()
    {
        if (_devices == null || !_devices.Any())
        {
            RefreshOutputDeviceList();
        }

        if (_devices == null)
        {
            return Enumerable.Empty<Device>();
        }

        return _devices.Where(d => d.Type == OutputDeviceType.DirectSound);
    }

    public IEnumerable<Device> GetWasapiDevices()
    {
        if (_devices == null || !_devices.Any())
        {
            RefreshOutputDeviceList();
        }

        if (_devices == null)
        {
            return Enumerable.Empty<Device>();
        }

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
