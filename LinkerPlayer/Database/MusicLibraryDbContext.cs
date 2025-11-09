using LinkerPlayer.Models;
using Microsoft.EntityFrameworkCore;

namespace LinkerPlayer.Database;

public class MusicLibraryDbContext : DbContext
{
    public DbSet<MediaFile> Tracks
    {
        get; set;
    }
    public DbSet<Playlist> Playlists
    {
        get; set;
    }
    public DbSet<PlaylistTrack> PlaylistTracks
    {
        get; set;
    }

    public MusicLibraryDbContext(DbContextOptions<MusicLibraryDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // MediaFile: Configure all properties to persist to database
        modelBuilder.Entity<MediaFile>()
            .HasKey(m => m.Id);
        modelBuilder.Entity<MediaFile>()
            .HasIndex(m => m.Path)
            .IsUnique();
        modelBuilder.Entity<MediaFile>()
            .Property(m => m.Id)
            .HasMaxLength(36);
        modelBuilder.Entity<MediaFile>()
            .Property(m => m.Path)
            .HasMaxLength(256)
            .IsRequired();

        // Metadata columns - all persisted
        modelBuilder.Entity<MediaFile>()
            .Property(m => m.FileName)
            .HasMaxLength(255);
        modelBuilder.Entity<MediaFile>()
            .Property(m => m.Title)
            .HasMaxLength(128);
        modelBuilder.Entity<MediaFile>()
            .Property(m => m.Artist)
            .HasMaxLength(128);
        modelBuilder.Entity<MediaFile>()
            .Property(m => m.Album)
            .HasMaxLength(128);
        modelBuilder.Entity<MediaFile>()
            .Property(m => m.Performers)
            .HasMaxLength(256);
        modelBuilder.Entity<MediaFile>()
            .Property(m => m.Composers)
            .HasMaxLength(256);
        modelBuilder.Entity<MediaFile>()
            .Property(m => m.Genres)
            .HasMaxLength(128);
        modelBuilder.Entity<MediaFile>()
            .Property(m => m.Copyright)
            .HasMaxLength(128);
        modelBuilder.Entity<MediaFile>()
            .Property(m => m.Track);
        modelBuilder.Entity<MediaFile>()
            .Property(m => m.TrackCount);
        modelBuilder.Entity<MediaFile>()
            .Property(m => m.Disc);
        modelBuilder.Entity<MediaFile>()
            .Property(m => m.DiscCount);
        modelBuilder.Entity<MediaFile>()
            .Property(m => m.Year);
        modelBuilder.Entity<MediaFile>()
            .Property(m => m.Duration);
        modelBuilder.Entity<MediaFile>()
            .Property(m => m.Bitrate);
        modelBuilder.Entity<MediaFile>()
            .Property(m => m.SampleRate);
        modelBuilder.Entity<MediaFile>()
            .Property(m => m.Channels);

        // Runtime-only properties - ignored for database
        modelBuilder.Entity<MediaFile>().Ignore(m => m.AlbumCover);
        modelBuilder.Entity<MediaFile>().Ignore(m => m.State);
        modelBuilder.Entity<MediaFile>().Ignore(m => m.PlaylistTracks);

        // Playlist
        modelBuilder.Entity<Playlist>()
            .HasIndex(p => p.Name)
            .IsUnique();
        modelBuilder.Entity<Playlist>()
            .Property(p => p.Name)
            .HasMaxLength(100);
        modelBuilder.Entity<Playlist>()
            .Property(p => p.SelectedTrackId)
            .HasMaxLength(36);
        modelBuilder.Entity<Playlist>()
            .Ignore(p => p.TrackIds);
        modelBuilder.Entity<Playlist>()
            .HasOne(p => p.SelectedTrackNavigation)
            .WithMany()
            .HasForeignKey(p => p.SelectedTrackId)
            .OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<Playlist>()
            .Property(p => p.SelectedTrackId)
            .IsRequired(false);

        // PlaylistTrack: Many-to-many between Playlist and MediaFile
        modelBuilder.Entity<PlaylistTrack>()
            .HasKey(pt => new { pt.PlaylistId, pt.TrackId });
        modelBuilder.Entity<PlaylistTrack>()
            .Property(pt => pt.TrackId)
            .HasMaxLength(36)
            .IsRequired();
        modelBuilder.Entity<PlaylistTrack>()
            .HasOne(pt => pt.Playlist)
            .WithMany(p => p.PlaylistTracks)
            .HasForeignKey(pt => pt.PlaylistId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<PlaylistTrack>()
            .HasOne(pt => pt.Track)
            .WithMany(t => t.PlaylistTracks)
            .HasForeignKey(pt => pt.TrackId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
