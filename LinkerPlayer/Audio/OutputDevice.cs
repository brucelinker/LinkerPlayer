using NAudio.Wave;
using System;
using System.Collections.Generic;

namespace LinkerPlayer.Audio;

public class OutputDevice
{
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