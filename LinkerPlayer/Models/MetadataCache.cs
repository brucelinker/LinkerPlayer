namespace LinkerPlayer.Models;

public class MetadataCache
{
    public string Path { get; set; } = string.Empty;
    public long LastModified
    {
        get; set;
    }
    public string Metadata { get; set; } = string.Empty;
}
