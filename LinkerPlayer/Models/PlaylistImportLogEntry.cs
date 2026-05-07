namespace LinkerPlayer.Models;

public enum PlaylistImportAction
{
    Recovered,
    Missing,
    Skipped
}

public class PlaylistImportLogEntry
{
    public DateTime Time { get; set; } = DateTime.Now;
    public PlaylistImportAction Action { get; set; }
    public string Path { get; set; } = string.Empty;
    public string? RecoveredPath { get; set; }

    public string ActionLabel => Action switch
    {
        PlaylistImportAction.Recovered => "Recovered",
        PlaylistImportAction.Missing   => "Missing",
        PlaylistImportAction.Skipped   => "Skipped",
        _                              => "Info"
    };

    public string Detail => Action == PlaylistImportAction.Recovered && RecoveredPath != null
        ? $"{System.IO.Path.GetFileName(Path)}  →  {RecoveredPath}"
        : System.IO.Path.GetFileName(Path);
}
