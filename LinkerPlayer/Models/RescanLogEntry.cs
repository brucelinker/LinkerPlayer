using System;

namespace LinkerPlayer.Models;

public enum RescanAction
{
    Info,
    Added,
    Removed,
    Updated,
    Warning
}

public class RescanLogEntry
{
    public DateTime Time    { get; set; } = DateTime.Now;
    public RescanAction Action { get; set; } = RescanAction.Info;
    public string Detail  { get; set; } = string.Empty;

    public string ActionLabel => Action switch
    {
        RescanAction.Added   => "Added",
        RescanAction.Removed => "Removed",
        RescanAction.Updated => "Updated",
        RescanAction.Warning => "Warning",
        _                    => "Info"
    };
}
