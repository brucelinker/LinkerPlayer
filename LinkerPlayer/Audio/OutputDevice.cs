using NAudio.Wave;
using System;
using System.Collections.Generic;

namespace LinkerPlayer.Audio;

public static class OutputDevice
{
    public static void InitializeOutputDevice()
    {
        if (string.IsNullOrEmpty(Properties.Settings.Default.MainOutputDevice))
        {
            Properties.Settings.Default.MainOutputDevice = GetOutputDeviceNameById(-1);
        }
        else if (!GetOutputDevicesList().Contains(Properties.Settings.Default.MainOutputDevice))
        {
            Properties.Settings.Default.MainOutputDevice = GetOutputDeviceNameById(-1);
        }

        if (string.IsNullOrEmpty(Properties.Settings.Default.AdditionalOutputDevice))
        {
            foreach (string outputDevice in GetOutputDevicesList())
            {
                if (outputDevice.Contains("virtual", StringComparison.OrdinalIgnoreCase))
                {
                    Properties.Settings.Default.AdditionalOutputDevice = outputDevice;
                }
            }
        }
        else if (!GetOutputDevicesList().Contains(Properties.Settings.Default.AdditionalOutputDevice))
        {
            Properties.Settings.Default.AdditionalOutputDevice = "";

            foreach (string outputDevice in GetOutputDevicesList())
            {
                if (outputDevice.Contains("virtual", StringComparison.OrdinalIgnoreCase))
                {
                    Properties.Settings.Default.AdditionalOutputDevice = outputDevice;
                }
            }
        }
    }

    public static string GetCurrentDeviceName()
    {
        return Properties.Settings.Default.MainOutputDevice;
    }

    public static int GetCurrentDeviceId()
    {
        return GetOutputDeviceId(Properties.Settings.Default.MainOutputDevice);
    }

    public static int GetOutputDeviceId(string nameDevice)
    {
        if (String.IsNullOrWhiteSpace(nameDevice))
        {
            throw new ArgumentNullException(nameof(nameDevice));
        }

        for (int n = -1; n < WaveOut.DeviceCount; n++)
        {
            if (nameDevice == WaveOut.GetCapabilities(n).ProductName)
            {
                return n;
            }
        }

        return 0;
    }

    public static List<string> GetOutputDevicesList()
    {
        var list = new List<string>();

        for (int n = -1; n < WaveOut.DeviceCount; n++)
        {
            list.Add(WaveOut.GetCapabilities(n).ProductName);
        }

        return list;
    }

    public static string GetOutputDeviceNameById(int id)
    {
        if (WaveOut.DeviceCount <= id)
        {
            return WaveOut.GetCapabilities(0).ProductName;
        }

        return WaveOut.GetCapabilities(id).ProductName;
    }
}