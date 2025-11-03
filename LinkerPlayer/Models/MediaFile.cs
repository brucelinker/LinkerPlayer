using CommunityToolkit.Mvvm.ComponentModel;
using LinkerPlayer.Core;
using ManagedBass;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Windows.Media.Imaging;
using TagLib;
using File = TagLib.File;

namespace LinkerPlayer.Models;

public interface IMediaFile
{
    string Id
    {
        get;
    }
    string Path
    {
        get;
    }
    string FileName
    {
        get;
    }
    uint Track
    {
        get;
    }
    uint TrackCount
    {
        get;
    }
    uint Disc
    {
        get;
    }
    uint DiscCount
    {
        get;
    }
    uint Year
    {
        get;
    }
    string Title
    {
        get;
    }
    string Artist
    {
        get;
    }
    string Album
    {
        get;
    }
    string Performers
    {
        get;
    }
    string Composers
    {
        get;
    }
    string Genres
    {
        get;
    }
    TimeSpan Duration
    {
        get;
    }
    string Comment
    {
        get;
    }
    int Bitrate
    {
        get;
    }
    int SampleRate
    {
        get;
    }
    int Channels
    {
        get;
    }
    string Copyright
    {
        get;
    }
    BitmapImage? AlbumCover
    {
        get;
    }
    PlaybackState State
    {
        get; set;
    }
}

[Index(nameof(Id), nameof(Path), IsUnique = true)]
public partial class MediaFile : ObservableValidator, IMediaFile
{
    private const string UnknownString = "<Unknown>";
    private CoverManager? _coverManager; // Make it nullable and lazy

    // Helper properties to get services when needed
    private IMediaFileHelper? MediaFileHelper => App.AppHost?.Services?.GetService<IMediaFileHelper>();
    private ILogger<MediaFile>? Logger => App.AppHost?.Services?.GetService<ILogger<MediaFile>>();

    // Lazy initialization of CoverManager
    private CoverManager CoverManager => _coverManager ??= new CoverManager();

    [Key]
    [StringLength(36, ErrorMessage = "Id must be a valid GUID (36 characters)")]
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString();

    [StringLength(256, ErrorMessage = "Path cannot exceed 256 characters")]
    [ObservableProperty]
    private string _path = string.Empty;

    [NotMapped]
    [StringLength(255, ErrorMessage = "FileName cannot exceed 255 characters")]
    [ObservableProperty]
    private string _fileName = string.Empty;

    [NotMapped]
    [StringLength(128, ErrorMessage = "Title cannot exceed 128 characters")]
    [ObservableProperty]
    private string _title = string.Empty;

    [NotMapped]
    [StringLength(128, ErrorMessage = "Artist cannot exceed 128 characters")]
    [ObservableProperty]
    private string _artist = string.Empty;

    [NotMapped]
    [StringLength(128, ErrorMessage = "Album cannot exceed 128 characters")]
    [ObservableProperty]
    private string _album = string.Empty;

    [NotMapped]
    [StringLength(256, ErrorMessage = "Performers cannot exceed 256 characters")]
    [ObservableProperty]
    private string _performers = string.Empty;

    [NotMapped]
    [StringLength(256, ErrorMessage = "Composers cannot exceed 256 characters")]
    [ObservableProperty]
    private string _composers = string.Empty;

    [NotMapped]
    [StringLength(128, ErrorMessage = "Genres cannot exceed 128 characters")]
    [ObservableProperty]
    private string _genres = string.Empty;

    [NotMapped]
    [StringLength(128, ErrorMessage = "Copyright cannot exceed 128 characters")]
    [ObservableProperty]
    private string _copyright = string.Empty;

    [NotMapped]
    //[StringLength(256, ErrorMessage = $"Comment cannot exceed 256 characters")]
    [ObservableProperty]
    private string _comment = string.Empty;

    [NotMapped]
    [ObservableProperty]
    private uint _track;

    [NotMapped]
    [ObservableProperty]
    private uint _trackCount;

    [NotMapped]
    [ObservableProperty]
    private uint _disc;

    [NotMapped]
    [ObservableProperty]
    private uint _discCount;

    [NotMapped]
    [ObservableProperty]
    private uint _year;

    [NotMapped]
    [ObservableProperty]
    private TimeSpan _duration;

    [NotMapped]
    [ObservableProperty]
    private int _bitrate;

    [NotMapped]
    [ObservableProperty]
    private int _sampleRate;

    [NotMapped]
    [ObservableProperty]
    private int _channels;

    [NotMapped]
    [ObservableProperty]
    private BitmapImage? _albumCover;

    [NotMapped]
    [ObservableProperty]
    private PlaybackState _state = PlaybackState.Stopped;

    [NotMapped]
    public List<PlaylistTrack> PlaylistTracks { get; set; } = new();

    public MediaFile()
    {
    }

    public MediaFile(string fileName, MediaFileHelper? mediaFileHelper = null, ILogger<MediaFile>? logger = null)
    {
        Path = fileName;
        FileName = System.IO.Path.GetFileName(fileName);
        UpdateFromFileMetadata(false);
    }

    public void UpdateFromFileMetadata(bool raisePropertyChanged = true)
    {
        if (string.IsNullOrWhiteSpace(Path))
        {
            return;
        }

        try
        {
            using File? file = File.Create(Path);

            // Only generate a new ID if we don't have one yet
            if (string.IsNullOrEmpty(Id) || Id == Guid.Empty.ToString())
            {
                Id = ValidateStringLength(Guid.NewGuid().ToString(), 36, nameof(Id));
            }

            FileName = ValidateStringLength(System.IO.Path.GetFileName(Path), 255, nameof(FileName));
            Title = ValidateStringLength(file.Tag.Title ?? FileName, 128, nameof(Title));
            Album = ValidateStringLength(file.Tag.Album ?? UnknownString, 128, nameof(Album));

            // Use the MediaFileHelper service to get the best album artist field
            string albumArtist = MediaFileHelper?.GetBestAlbumArtistField(file.Tag) ?? UnknownString;
            Artist = ValidateStringLength(albumArtist, 128, nameof(Artist));

            List<string> performers = file.Tag.Performers.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            Performers = ValidateStringLength(performers.Count > 0 ? string.Join("/", performers) : file.Tag.FirstPerformer ?? string.Empty, 256, nameof(Performers));

            if (string.IsNullOrWhiteSpace(Artist))
            {
                Artist = ValidateStringLength(string.IsNullOrWhiteSpace(Performers) ? UnknownString : Performers, 128, nameof(Artist));
            }

            //Comment = ValidateStringLength(file.Tag.Comment ?? string.Empty, 256, nameof(Comment));

            List<string> composers = file.Tag.Composers.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            Composers = ValidateStringLength(composers.Count > 1 ? string.Join("/", composers) : file.Tag.FirstComposer ?? string.Empty, 256, nameof(Composers));

            Copyright = ValidateStringLength(file.Tag.Copyright ?? string.Empty, 128, nameof(Copyright));

            Genres = ValidateStringLength(file.Tag.Genres.Length > 1 ? string.Join("/", file.Tag.Genres) : file.Tag.FirstGenre ?? string.Empty, 128, nameof(Genres));

            Track = file.Tag.Track;
            TrackCount = file.Tag.TrackCount;
            Disc = file.Tag.Disc;
            DiscCount = file.Tag.DiscCount;
            Year = file.Tag.Year;
            Bitrate = file.Properties.AudioBitrate;
            SampleRate = file.Properties.AudioSampleRate;
            Channels = file.Properties.AudioChannels;

            if (file.Properties.MediaTypes != MediaTypes.None)
            {
                Duration = file.Properties.Duration != TimeSpan.Zero ? file.Properties.Duration : TimeSpan.FromSeconds(1);
            }
            else
            {
                Duration = TimeSpan.FromSeconds(1);
            }

            if (raisePropertyChanged)
            {
                ValidateAllProperties();
            }
        }
        catch (TagLib.CorruptFileException ex)
        {
            Logger?.LogError(ex, "Corrupted file {FileName}: {Message}", Path, ex.Message);
            SetFallbackMetadata(raisePropertyChanged);
        }
        catch (TagLib.UnsupportedFormatException ex)
        {
            Logger?.LogError(ex, "Unsupported format for {FileName}: {Message}", Path, ex.Message);
            SetFallbackMetadata(raisePropertyChanged);
        }
        catch (ArgumentException ex) when (ex.ParamName == "ident" && ex.Message.Contains("identifier must be four bytes long"))
        {
            Logger?.LogError(ex, "Invalid metadata identifiers in {FileName}: {Message}", Path, ex.Message);
            SetFallbackMetadata(raisePropertyChanged);
        }
        catch (Exception e)
        {
            Logger?.LogError(e, "TagLib.File.Create failed for {FileName}: {Message}", Path, e.Message);
            SetFallbackMetadata(raisePropertyChanged);
        }
    }

    private void SetFallbackMetadata(bool raisePropertyChanged)
    {
        Title = ValidateStringLength(FileName, 128, nameof(Title));
        Album = ValidateStringLength(UnknownString, 128, nameof(Album));
        Artist = ValidateStringLength(UnknownString, 128, nameof(Artist));
        Performers = string.Empty;
        Composers = string.Empty;
        Copyright = string.Empty;
        Genres = string.Empty;
        Comment = string.Empty;
        Track = 0;
        TrackCount = 0;
        Disc = 0;
        DiscCount = 0;
        Year = 0;
        Bitrate = 0;
        SampleRate = 0;
        Channels = 0;
        Duration = TimeSpan.FromSeconds(1);

        if (raisePropertyChanged)
        {
            ValidateAllProperties();
        }
    }

    public void LoadAlbumCover()
    {
        try
        {
            BitmapImage? image = CoverManager.GetImageFromPictureTag(Path); // Use property instead of field
            if (image != null)
            {
                // Force reload from file, not cache
                if (image.UriSource != null)
                {
                    BitmapImage bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = image.UriSource;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    AlbumCover = bitmap;
                }
                else
                {
                    AlbumCover = image;
                }
            }
            else
            {
                AlbumCover = null;
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to load album cover for {Path}", Path);
            AlbumCover = null;
        }
    }

    public void UpdateFullMetadata()
    {
        UpdateFromFileMetadata();
    }

    public override string ToString()
    {
        return $"{Track} {Artist} - {Title} {Duration:m\\:ss}";
    }

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
            State = State
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

    public static string GetTagLibFileJson(string filePath)
    {
        try
        {
            using File? file = File.Create(filePath);
            var tagInfo = new
            {
                FileName = file.Name,
                Tag = new
                {
                    file.Tag.Title,
                    file.Tag.Album,
                    AlbumArtists = file.Tag.AlbumArtists,
                    file.Tag.FirstAlbumArtist,
                    file.Tag.Performers,
                    file.Tag.FirstPerformer,
                    file.Tag.Composers,
                    file.Tag.FirstComposer,
                    file.Tag.Genres,
                    file.Tag.FirstGenre,
                    file.Tag.Year,
                    file.Tag.Track,
                    file.Tag.TrackCount,
                    file.Tag.Disc,
                    file.Tag.DiscCount,
                    file.Tag.Comment,
                    file.Tag.Copyright,
                    file.Tag.Lyrics,
                    file.Tag.BeatsPerMinute,
                    file.Tag.Conductor,
                    file.Tag.Grouping,
                    file.Tag.Pictures
                },
                Properties = new
                {
                    file.Properties.AudioBitrate,
                    file.Properties.AudioSampleRate,
                    file.Properties.AudioChannels,
                    file.Properties.Duration,
                    file.Properties.MediaTypes,
                    file.Properties.Description
                }
            };
            return System.Text.Json.JsonSerializer.Serialize(tagInfo, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
        catch (TagLib.CorruptFileException ex)
        {
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                Error = $"Corrupted file: {ex.Message}"
            });
        }
        catch (TagLib.UnsupportedFormatException ex)
        {
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                Error = $"Unsupported format: {ex.Message}"
            });
        }
        catch (ArgumentException ex) when (ex.ParamName == "ident" && ex.Message.Contains("identifier must be four bytes long"))
        {
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                Error = $"Invalid metadata identifiers: {ex.Message}"
            });
        }
        catch (Exception ex)
        {
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                Error = $"Error reading file: {ex.Message}"
            });
        }
    }
}
