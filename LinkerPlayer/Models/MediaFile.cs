using ATL;
using CommunityToolkit.Mvvm.ComponentModel;
using LinkerPlayer.Audio;
using LinkerPlayer.Core;
using LinkerPlayer.Services;
using ManagedBass;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.Windows.Media.Imaging;

namespace LinkerPlayer.Models;

public enum TrackSource
{
    Manual,
    WatchedFolder
}

public interface IMediaFile
{
    string Id { get; }
    string Path { get; }
    string FileName { get; }
    int Track { get; }
    int TrackCount { get; }
    int Disc { get; }
    int DiscCount { get; }
    int Year { get; }
    string Title { get; }
    string Artist { get; }
    string Album { get; }
    string AlbumArtist { get; }
    string Performers { get; }
    string Composers { get; }
    string Genres { get; }
    int Duration { get; }
    string Comment { get; }
    int Bitrate { get; }
    double SampleRate { get; }
    int Channels { get; }
    string? Codec { get; }
    string? Copyright { get; }
    BitmapImage? AlbumCover { get; }
    PlaybackState State { get; set; }
}

[Index(nameof(Id), nameof(Path), IsUnique = true)]
public partial class MediaFile : ObservableValidator, IMediaFile
{
    private const string UnknownString = "<Unknown>";
    private CoverManager? _coverManager;
    private bool _isDirtyTrackingEnabled;

    private IMediaFileHelper? MediaFileHelper
    {
        get
        {
            try { return App.AppHost?.Services?.GetService<IMediaFileHelper>(); }
            catch { return null; }
        }
    }

    private ILogger<MediaFile>? Logger
    {
        get
        {
            try { return App.AppHost?.Services?.GetService<ILogger<MediaFile>>(); }
            catch { return null; }
        }
    }

    private CoverManager CoverManager => _coverManager ??= new CoverManager();

    /// <summary>
    /// Set of property names that have been edited by the user in the Library DataGrid.
    /// </summary>
    [NotMapped]
    public HashSet<string> DirtyProperties { get; } = new();

    /// <summary>
    /// Properties that can be edited inline in the Library DataGrid.
    /// </summary>
    private static readonly HashSet<string> EditableProperties = new()
    {
        nameof(Title), nameof(Artist), nameof(Album), nameof(AlbumArtist),
        nameof(Genres), nameof(Track), nameof(TrackCount), nameof(Disc),
        nameof(DiscCount), nameof(Year), nameof(Composers), nameof(Comment),
        nameof(Copyright)
    };

    [NotMapped]
    public bool IsDirty => DirtyProperties.Count > 0;

    [NotMapped]
    public int DirtyCount => DirtyProperties.Count;

    public bool IsPropertyDirty(string propertyName) => DirtyProperties.Contains(propertyName);

    public void ClearDirty()
    {
        List<string> previouslyDirty = DirtyProperties.ToList();
        DirtyProperties.Clear();
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(DirtyCount));
        foreach (string prop in previouslyDirty)
        {
            OnPropertyChanged($"IsDirty_{prop}");
        }
    }

    /// <summary>
    /// Enables dirty tracking for user edits. Call after loading metadata to avoid marking
    /// programmatic property sets as dirty.
    /// </summary>
    public void EnableDirtyTracking() => _isDirtyTrackingEnabled = true;

    public void DisableDirtyTracking() => _isDirtyTrackingEnabled = false;

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (_isDirtyTrackingEnabled &&
            e.PropertyName != null &&
            EditableProperties.Contains(e.PropertyName) &&
            !DirtyProperties.Contains(e.PropertyName))
        {
            DirtyProperties.Add(e.PropertyName);
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsDirty)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(DirtyCount)));
            OnPropertyChanged(new PropertyChangedEventArgs($"IsDirty_{e.PropertyName}"));
        }
    }

    [Key]
    [StringLength(36)]
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString();

    [ObservableProperty]
    private string _path = string.Empty;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _artist = string.Empty;

    [ObservableProperty]
    private string _album = string.Empty;

    [ObservableProperty]
    private string _albumArtist = string.Empty;

    [ObservableProperty]
    private string _performers = string.Empty;

    [ObservableProperty]
    private string _composers = string.Empty;

    [ObservableProperty]
    private string _genres = string.Empty;

    [ObservableProperty]
    private string? _copyright;

    [ObservableProperty]
    private string _comment = string.Empty;

    [ObservableProperty]
    private int _track;

    [ObservableProperty]
    private int _trackCount;

    [ObservableProperty]
    private int _disc;

    [ObservableProperty]
    private int _discCount;

    [ObservableProperty]
    private int _year;

    [ObservableProperty]
    private int _duration;

    [ObservableProperty]
    private int _bitrate;

    [ObservableProperty]
    private double _sampleRate;

    [ObservableProperty]
    private int _channels;

    [ObservableProperty]
    private string? _codec = string.Empty;

    [ObservableProperty]
    private int? _leadingSilenceMs;

    [ObservableProperty]
    private int? _trailingSilenceMs;

    [ObservableProperty]
    private DateTime? _fileLastWriteTimeUtc;

    [ObservableProperty]
    private DateTime? _lastMetadataRefreshUtc;

    [ObservableProperty]
    private bool _needsMetadataRefresh;

    [property: NotMapped]
    [ObservableProperty]
    private TrackSource _source = TrackSource.Manual;

    [property: NotMapped]
    [ObservableProperty]
    private string _watchedFolderPath = string.Empty;

    [NotMapped]
    [ObservableProperty]
    private BitmapImage? _albumCover;

    /// <summary>
    /// Set when <see cref="LoadAlbumCover"/> encounters an embedded image in a format
    /// WPF cannot decode (e.g. "WebP", "AVIF"). Null when the cover loaded successfully
    /// or there is no embedded picture at all.
    /// </summary>
    [NotMapped]
    public string? UnsupportedCoverFormat { get; private set; }

    [NotMapped] 
    [ObservableProperty]
    private PlaybackState _state = PlaybackState.Stopped;

    [NotMapped]
    [ObservableProperty]
    private bool _isRefreshing;

    [NotMapped]
    public List<PlaylistTrack> PlaylistTracks { get; set; } = new();

    public MediaFile() { }

    public MediaFile(string fileName, MediaFileHelper? mediaFileHelper = null, ILogger<MediaFile>? logger = null)
    {
        Path = fileName;
        FileName = System.IO.Path.GetFileName(fileName);
        NeedsMetadataRefresh = true;
        LastMetadataRefreshUtc = null;
    }

    public void UpdateFromFileMetadata(bool raisePropertyChanged = true)
    {
        if (string.IsNullOrWhiteSpace(Path))
            return;

        DisableDirtyTracking();
        Track? track = null;

        try
        {
            track = new Track(Path);   // ATL auto-detects format (including AC3)
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            try { Logger?.LogWarning(ex, "Failed to read metadata with ATL for {Path}", Path); } catch { }
            try
            {
                IImportErrorLogger? importLogger = App.AppHost?.Services?.GetService<Services.IImportErrorLogger>();
                importLogger?.Log(Path, ex);
            }
            catch { }
            SetFallbackMetadata(raisePropertyChanged);
            return;
        }

        // Only generate a new ID if needed
        if (string.IsNullOrEmpty(Id) || Id == Guid.Empty.ToString())
        {
            Id = ValidateStringLength(Guid.NewGuid().ToString(), 36, nameof(Id));
        }

        FileName = ValidateStringLength(System.IO.Path.GetFileName(Path), 255, nameof(FileName));

        Title = track.Title ?? FileName;

        string artist = MediaFileHelper?.GetBestArtistField(track) ?? UnknownString;
        Artist = artist;

        Album = track.Album ?? UnknownString;

        string albumArtist = MediaFileHelper?.GetBestAlbumArtistField(track) ?? UnknownString;
        AlbumArtist = albumArtist;

        // ATL uses single string for these (no array like TagLib)
        Performers = track.Artist ?? string.Empty;
        Composers = track.Composer ?? string.Empty;
        Genres = track.Genre ?? string.Empty;

        Copyright = track.Copyright ?? string.Empty;
        Comment = track.Comment ?? string.Empty;

        Track = track.TrackNumber ?? 0;
        TrackCount = track.TrackTotal ?? 0;
        Disc = track.DiscNumber ?? 0;
        DiscCount = track.DiscTotal ?? 0;
        Year = track.Year ?? 0;
        Logger?.LogDebug("ATL Year for {Path}: {AtlYear}, Set Year to {Year}", Path, track.Year, Year);

        Bitrate = track.Bitrate;
        SampleRate = track.SampleRate;
        Channels = track.ChannelsArrangement?.NbChannels ?? 0;

        Codec = !string.IsNullOrWhiteSpace(Path)
            ? System.IO.Path.GetExtension(Path).TrimStart('.').ToUpperInvariant()
            : track.AudioFormat?.ShortName?.ToUpperInvariant()
              ?? track.CodecFamily.ToString().ToUpperInvariant()
              ?? string.Empty;

        // ATL.Duration is seconds (int); store as seconds (int)
        try
        {
            int atlDurationSeconds = track.Duration;
            Logger?.LogDebug("ATL Duration for {Path}: {DurationSeconds}s", Path, atlDurationSeconds);

            // Check if ATL duration is reasonable (> 0 for audio files)
            if (atlDurationSeconds > 0)
            {
                Duration = atlDurationSeconds;
                Logger?.LogDebug("Set Duration to {Duration}s from ATL", Duration);
            }
            else
            {
                Logger?.LogWarning("ATL Duration is 0 for {Path}, trying BASS fallback", Path);
                // Fallback: try to get duration using BASS
                try
                {
                    // Ensure BASS is initialized for decoding
                    object? engineObj = App.AppHost?.Services?.GetService(typeof(AudioEngine));
                    AudioEngine? engine = engineObj as AudioEngine;
                    if (engine != null && !engine.IsBassInitialized)
                    {
                        Logger?.LogDebug("Initializing BASS for duration extraction");
                        engine.InitializeAudioDevice();
                    }

                    // Try to create stream for duration calculation
                    int stream = ManagedBass.Bass.CreateStream(Path, 0, 0, ManagedBass.BassFlags.Decode);
                    if (stream != 0)
                    {
                        long len = ManagedBass.Bass.ChannelGetLength(stream);
                        double seconds = ManagedBass.Bass.ChannelBytes2Seconds(stream, len);
                        Duration = (int)seconds;
                        Logger?.LogDebug("Set Duration to {Duration}s from BASS", Duration);
                        ManagedBass.Bass.StreamFree(stream);
                    }
                    else
                    {
                        Logger?.LogWarning("BASS stream creation failed for {Path}: {Error}", Path, ManagedBass.Bass.LastError);
                        Duration = 0;
                    }
                }
                catch (Exception ex)
                {
                    Logger?.LogWarning(ex, "Failed to get duration using BASS for {Path}", Path);
                    Duration = 0;
                }
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to get duration for {Path}", Path);
            Duration = 0;
        }

        if (raisePropertyChanged)
        {
            // Optional: OnPropertyChanged for all if needed
        }

        EnableDirtyTracking();
    }

    private void SetFallbackMetadata(bool raisePropertyChanged)
    {
        Title = FileName;
        Album = UnknownString;
        Artist = UnknownString;
        AlbumArtist = UnknownString;
        Performers = string.Empty;
        Composers = string.Empty;
        Genres = string.Empty;
        Copyright = string.Empty;
        Comment = string.Empty;

        Track = 0;
        TrackCount = 0;
        Disc = 0;
        DiscCount = 0;
        Year = 0;

        Bitrate = 0;
        SampleRate = 0;
        Channels = 2; // Default to stereo
        Duration = 0;
        Codec = string.Empty;
    }

    // WPF's built-in imaging pipeline only supports these MIME types natively.
    // Modern formats like WebP/AVIF/HEIF require OS codec extensions that may not
    // be present or may be unloaded, causing NotSupportedException at EndInit().
    private static readonly HashSet<string> SupportedCoverMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/jpg", "image/png", "image/bmp",
        "image/gif", "image/tiff", "image/x-tiff"
    };

    public void LoadAlbumCover()
    {
        try
        {
            Track track = new Track(Path);

            // Prefer front cover first, then fall back to any embedded picture
            PictureInfo? pic = track.EmbeddedPictures.FirstOrDefault(p =>
                p.PicType == PictureInfo.PIC_TYPE.Front ||
                p.PicType == PictureInfo.PIC_TYPE.CD ||
                p.PicType == PictureInfo.PIC_TYPE.Generic)
                ?? track.EmbeddedPictures.FirstOrDefault();

            if (pic?.PictureData is not { Length: > 0 })
            {
                AlbumCover = null;
                UnsupportedCoverFormat = null;
                return;
            }

            // Skip formats WPF can't decode natively (e.g. WebP, AVIF, HEIF)
            if (!string.IsNullOrEmpty(pic.MimeType) && !SupportedCoverMimeTypes.Contains(pic.MimeType))
            {
                // Derive a friendly label from the MIME type: "image/webp" → "WebP"
                string label = pic.MimeType.Contains('/')
                    ? pic.MimeType[(pic.MimeType.LastIndexOf('/') + 1)..].ToUpperInvariant()
                    : pic.MimeType.ToUpperInvariant();
                Logger?.LogDebug("Skipping unsupported cover format '{MimeType}' for {Path}", pic.MimeType, Path);
                AlbumCover = null;
                UnsupportedCoverFormat = label;
                return;
            }

            using MemoryStream ms = new MemoryStream(pic.PictureData);
            BitmapImage bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = ms;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;  // Reads all data before EndInit returns; safe to dispose stream after
            bitmap.EndInit();
            bitmap.Freeze();
            AlbumCover = bitmap;
            UnsupportedCoverFormat = null;
        }
        catch (NotSupportedException)
        {
            // Image data is in a format WPF can't decode (codec not available on this machine)
            Logger?.LogDebug("Cover image format not supported by WPF decoder for {Path}", Path);
            AlbumCover = null;
            UnsupportedCoverFormat = "Unknown Format";
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to load album cover for {Path}", Path);
            AlbumCover = null;
            UnsupportedCoverFormat = null;
        }
    }

    public void UpdateFullMetadata() => UpdateFromFileMetadata();

    public override string ToString() => $"{Track} {Artist} - {Title} {Duration:m\\:ss}";

    public MediaFile Clone()
    {
        return new MediaFile
        {
            Id = Id,
            Path = Path,
            FileName = FileName,
            Track = Track,
            TrackCount = TrackCount,
            Disc = Disc,
            DiscCount = DiscCount,
            Year = Year,
            Title = Title,
            Album = Album,
            Artist = Artist,
            AlbumArtist = AlbumArtist,
            Performers = Performers,
            Composers = Composers,
            Genres = Genres,
            Comment = Comment,
            Duration = Duration,
            Bitrate = Bitrate,
            SampleRate = SampleRate,
            Channels = Channels,
            Copyright = Copyright,
            AlbumCover = AlbumCover,
            State = State,
            Codec = Codec
        };
    }

    private string ValidateStringLength(string value, int maxLength, string propertyName)
    {
        if (value.Length > maxLength)
        {
            Logger?.LogWarning(
                "Property {PropertyName} exceeds maximum length of {MaxLength} characters for file {FileName}. Truncating.",
                propertyName, maxLength, FileName);
            return value.Substring(0, maxLength);
        }
        return value;
    }

    // Optional: ATL version of debug JSON (if you still need it)
    public static string GetAtlFileJson(string filePath)
    {
        try
        {
            Track track = new Track(filePath);
            var info = new
            {
                FileName = System.IO.Path.GetFileName(filePath),
                Metadata = new
                {
                    track.Title,
                    track.Artist,
                    track.Album,
                    track.AlbumArtist,
                    track.Composer,
                    track.Genre,
                    track.Year,
                    track.TrackNumber,
                    track.TrackTotal,
                    track.DiscNumber,
                    track.DiscTotal,
                    track.Comment,
                    track.Copyright,
                    track.Lyrics
                },
                AudioProperties = new
                {
                    track.Bitrate,
                    track.SampleRate,
                    Channels = track.ChannelsArrangement.NbChannels,
                    Layout = track.ChannelsArrangement.Description,
                    Duration = track.Duration,
                    Codec = track.CodecFamily
                }
            };
            return System.Text.Json.JsonSerializer.Serialize(info, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return System.Text.Json.JsonSerializer.Serialize(new { Error = ex.Message });
        }
    }
}
