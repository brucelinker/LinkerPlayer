using System.Collections.Generic;

namespace LinkerPlayer.Models;

public class BandsSettings
{
    public string Name = string.Empty;
    public bool Locked = false;
    public List<EqualizerBandSettings>? EqualizerBands = new();
}