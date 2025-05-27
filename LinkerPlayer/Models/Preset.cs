using System.Collections.Generic;

namespace LinkerPlayer.Models;

public class Preset
{
    public string? Name;
    public bool Locked = false;
    public List<EqualizerBandSettings>? EqualizerBands = new();
}