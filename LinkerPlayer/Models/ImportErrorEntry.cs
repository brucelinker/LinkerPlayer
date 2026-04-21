using System;

namespace LinkerPlayer.Models;

public class ImportErrorEntry
{
    public string Path { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime Time { get; set; } = DateTime.UtcNow;
}
