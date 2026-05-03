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

public enum TrackHealthStatus
{
    Unknown,   // not yet checked
    Ok,        // file exists and write-time matches
    Missing,   // file no longer exists on disk
    Changed    // file exists but metadata/write-time has drifted
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
    private readonly object _dirtyLock = new();
    private readonly HashSet<string> _dirtyProperties = new();

    [NotMapped]
    public IReadOnlyCollection<string> DirtyProperties
    {
        get { lock (_dirtyLock) { return _dirtyProperties.ToArray(); } }
    }

    /// <summary>
    /// Properties that are auto-dirtied when changed while dirty tracking is enabled.
    /// AlbumCover is intentionally absent: it is loaded by background infrastructure on
    /// scroll and must only become dirty via an explicit <see cref="MarkPropertyDirty"/> call
    /// from a user action (e.g. paste/drag cover art).
    /// </summary>
    private static readonly HashSet<string> EditableProperties = new()
    {
        nameof(Title), nameof(Artist), nameof(Album), nameof(AlbumArtist),
        nameof(Genres), nameof(Track), nameof(TrackCount), nameof(Disc),
        nameof(DiscCount), nameof(Year), nameof(Composers), nameof(Comment),
        nameof(Copyright)
    };

    [NotMapped]
    public bool IsDirty { get { lock (_dirtyLock) { return _dirtyProperties.Count > 0; } } }

    [NotMapped]
    public int DirtyCount { get { lock (_dirtyLock) { return _dirtyProperties.Count; } } }

    /// <summary>
    /// Set by the save pipeline immediately after ATL writes this file.
    /// The metadata refresher uses this to skip re-reading files that were just saved by
    /// this app — SMB/NAS write-flush and metadata-propagation delays mean the on-disk
    /// write-time may differ from our stamped value for 10–30 s, which would otherwise
    /// cause a spurious refresh attempt while the file handle is still held by the OS.
    /// </summary>
    [NotMapped]
    public DateTime? LastSavedByAppUtc { get; set; }

    public void MarkPropertyDirty(string propertyName)
    {
        bool added;
        lock (_dirtyLock)
        {
            added = _dirtyProperties.Add(propertyName);
        }

        if (added)
        {
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(DirtyCount));
        }
    }

    public bool IsPropertyDirty(string propertyName)
    {
        lock (_dirtyLock) { return _dirtyProperties.Contains(propertyName); }
    }

    public void ClearDirty()
    {
        string[] previouslyDirty;
        lock (_dirtyLock)
        {
            previouslyDirty = _dirtyProperties.ToArray();
            _dirtyProperties.Clear();
        }

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

    public bool IsDirtyTrackingEnabled => _isDirtyTrackingEnabled;

    /// <summary>
    /// Temporarily suspends dirty tracking for the duration of the returned scope.
    /// Restores the previous state on <see cref="IDisposable.Dispose"/>, even if an
    /// exception is thrown. Replaces the scattered <c>bool wasEnabled / Disable / if (wasEnabled) Enable</c>
    /// pattern — callers just write <c>using (track.SuspendDirtyTracking()) { … }</c>.
    /// </summary>
    public IDisposable SuspendDirtyTracking()
    {
        bool wasEnabled = _isDirtyTrackingEnabled;
        _isDirtyTrackingEnabled = false;
        return new DirtyTrackingScope(this, wasEnabled);
    }

    private sealed class DirtyTrackingScope : IDisposable
    {
        private readonly MediaFile _owner;
        private readonly bool _restore;
        private bool _disposed;

        internal DirtyTrackingScope(MediaFile owner, bool restore)
        {
            _owner = owner;
            _restore = restore;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_restore) _owner._isDirtyTrackingEnabled = true;
        }
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (_isDirtyTrackingEnabled &&
            e.PropertyName != null &&
            EditableProperties.Contains(e.PropertyName))
        {
            bool added;
            lock (_dirtyLock)
            {
                added = _dirtyProperties.Add(e.PropertyName);
            }

            if (added)
            {
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsDirty)));
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(DirtyCount)));
                OnPropertyChanged(new PropertyChangedEventArgs($"IsDirty_{e.PropertyName}"));
            }
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

    [property: NotMapped]
    [ObservableProperty]
    private TrackHealthStatus _healthStatus = TrackHealthStatus.Unknown;

    [NotMapped]
    [ObservableProperty]
    private BitmapImage? _albumCover;

    /// <summary>
    /// True when the track has a loaded album cover image in memory.
    /// For display-column use prefer <see cref="HasEmbeddedCover"/> which is set
    /// during the ATL metadata scan and does not require the image to be loaded.
    /// </summary>
    [NotMapped]
    public bool HasAlbumCover => AlbumCover != null || HasEmbeddedCover;

    partial void OnAlbumCoverChanged(BitmapImage? value) =>
        OnPropertyChanged(nameof(HasAlbumCover));

    /// <summary>
    /// Persisted flag: true when the audio file has at least one embedded picture tag,
    /// regardless of whether the image has been decoded into memory yet.
    /// Set during <see cref="UpdateFromFileMetadata"/> so it is known for every track
    /// immediately after the library loads — no per-track click required.
    /// </summary>
    [ObservableProperty]
    private bool _hasEmbeddedCover;

    partial void OnHasEmbeddedCoverChanged(bool value) =>
        OnPropertyChanged(nameof(HasAlbumCover));

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

        using IDisposable _ = SuspendDirtyTracking();
        Track? track = null;

        try
        {
            track = new Track(Path);   // ATL auto-detects format (including AC3)
        }
        catch (IOException)
        {
            // Let IOExceptions bubble up to callers (e.g. TrackMetadataRefresher) that
            // have retry logic for transient file-lock errors on UNC/NAS shares.
            throw;
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

        string ext = !string.IsNullOrWhiteSpace(Path)
            ? System.IO.Path.GetExtension(Path).TrimStart('.').ToUpperInvariant()
            : track.AudioFormat?.ShortName?.ToUpperInvariant()
              ?? track.CodecFamily.ToString().ToUpperInvariant()
              ?? string.Empty;

        Codec = string.Equals(ext, "MP3", StringComparison.OrdinalIgnoreCase)
            ? $"MP3 {(track.IsVBR ? "VBR" : "CBR")}"
            : ext;

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
                        try
                        {
                            long len = ManagedBass.Bass.ChannelGetLength(stream);
                            double seconds = ManagedBass.Bass.ChannelBytes2Seconds(stream, len);
                            Duration = (int)seconds;
                            Logger?.LogDebug("Set Duration to {Duration}s from BASS", Duration);
                        }
                        finally
                        {
                            ManagedBass.Bass.StreamFree(stream);
                        }
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

        // Set the lightweight cover-present flag from the ATL picture list.
        // This does NOT load image bytes — it is a cheap O(1) check that lets
        // the Library DataGrid show the cover indicator for every track on startup.
        HasEmbeddedCover = track.EmbeddedPictures.Count > 0;

        try { FileLastWriteTimeUtc = File.GetLastWriteTimeUtc(Path); } catch { }
    }

    private void SetFallbackMetadata(bool raisePropertyChanged)
    {
        // Only fill in fields that are genuinely empty — never overwrite real data
        // with placeholder values just because ATL failed to re-read the file.
        if (string.IsNullOrWhiteSpace(Title))       Title = FileName;
        if (string.IsNullOrWhiteSpace(Album))       Album = UnknownString;
        if (string.IsNullOrWhiteSpace(Artist))      Artist = UnknownString;
        if (string.IsNullOrWhiteSpace(AlbumArtist)) AlbumArtist = UnknownString;
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
        // Disable dirty tracking so setting AlbumCover here doesn't mark the track
        // as needing a save — this is a display-only load, not a user edit.
        using IDisposable _suspend = SuspendDirtyTracking();
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
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            AlbumCover = bitmap;
            UnsupportedCoverFormat = null;
        }
        catch (NotSupportedException)
        {
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
        finally
        {
            // ATL.Track has no IDisposable — nudge the GC to collect it promptly
            // so it releases the OS file handle on the UNC share.
            GC.Collect(0, GCCollectionMode.Optimized, blocking: false);
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
            HasEmbeddedCover = HasEmbeddedCover,
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
