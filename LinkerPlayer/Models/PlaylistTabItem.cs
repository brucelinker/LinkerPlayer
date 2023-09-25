using LinkerPlayer.UserControls;
using System.Collections.Generic;

namespace LinkerPlayer.Models;

public class PlaylistTabItem
{
    public string? Name { get; set; }
    public List<string> Songs { get; set; } = new List<string>();
}