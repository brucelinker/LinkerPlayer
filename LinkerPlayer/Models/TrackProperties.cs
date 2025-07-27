namespace LinkerPlayer.Models;

public class TrackProperties
{
    public string FileName { get; set; } = string.Empty;
    public Tag? Tag { get; set; }
    public Properties? Properties { get; set; }
}

public class Tag
{
    public string Title { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    public string[]? AlbumArtists { get; set; } = [];
    public string FirstAlbumArtist { get; set; } = string.Empty;
    public string[]? Performers { get; set; } = [];
    public string FirstPerformer { get; set; } = string.Empty;
    public string[]? Composers { get; set; } = [];
    public string FirstComposer { get; set; } = string.Empty;
    public string[]? Genres { get; set; } = [];
    public string FirstGenre { get; set; } = string.Empty;
    public uint Year { get; set; } = 0;
    public uint Track { get; set; } = 0;
    public uint TrackCount { get; set; } = 0;
    public uint Disc { get; set; } = 0;
    public uint DiscCount { get; set; } = 0;
    public string Comment { get; set; } = string.Empty;
    public string Copyright { get; set; } = string.Empty;
    public string Lyrics { get; set; } = string.Empty;
    public uint BeatsPerMinute { get; set; } = 0;
    public string Conductor { get; set; } = string.Empty;
    public string Grouping { get; set; } = string.Empty;
    public Picture[]? Pictures { get; set; } = [];
}

public class Picture
{
    public string MimeType { get; set; } = string.Empty;
    public int Type { get; set; } = 0;
    public string Filename { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    //public int[]? Data { get; set; }
}

public class Properties
{
    public int AudioBitrate { get; set; } = 0;
    public int AudioSampleRate { get; set; } = 0;
    public int AudioChannels { get; set; } = 0;
    public string Duration { get; set; } = string.Empty;
    public int MediaTypes { get; set; } = 0;
    public string Description { get; set; } = string.Empty;
}
