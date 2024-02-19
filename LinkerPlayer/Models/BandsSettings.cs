using System;
using System.Collections.Generic;
using NAudio.Extras;

namespace LinkerPlayer.Models;

public class BandsSettings
{
    public List<EqualizerBand>? EqualizerBands = new();
    public string Name = String.Empty;
}