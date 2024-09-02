using System;
using System.Collections.Generic;
using NAudio.Extras;

namespace LinkerPlayer.Models;

public class BandsSettings
{
    public string Name = String.Empty;
    public bool Locked = false;
    public List<EqualizerBand>? EqualizerBands = new();
}