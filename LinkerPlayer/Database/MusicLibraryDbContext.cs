using Microsoft.EntityFrameworkCore;
using LinkerPlayer.Models;

namespace LinkerPlayer.Database;

public class MusicLibraryDbContext : DbContext
{
    public DbSet<MediaFile> Tracks { get; set; }
    public DbSet<Playlist> Playlists { get; set; }
    public DbSet<PlaylistTrack> PlaylistTracks { get; set; }
    public DbSet<MetadataCache> MetadataCache { get; set; } // Added DbSet for MetadataCache

    public MusicLibraryDbContext(DbContextOptions<MusicLibraryDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MediaFile>()
            .HasIndex(t => new { t.Path, t.Album, t.Duration })
            .IsUnique();
        modelBuilder.Entity<MediaFile>()
            .Property(m => m.Id)
            .HasMaxLength(36); // GUID
        modelBuilder.Entity<MediaFile>()
            .Property(m => m.Path)
            .HasMaxLength(256); // File path
        modelBuilder.Entity<MediaFile>()
            .Property(m => m.FileName)
            .HasMaxLength(255); // File name (shorter than path)
        modelBuilder.Entity<MediaFile>()
            .Property(m => m.Title)
            .HasMaxLength(128); // Song title
        modelBuilder.Entity<MediaFile>()
            .Property(m => m.Artist)
            .HasMaxLength(128); // Artist name
        modelBuilder.Entity<MediaFile>()
            .Property(m => m.Album)
            .HasMaxLength(128); // Album name
        modelBuilder.Entity<MediaFile>()
            .Property(m => m.Performers)
            .HasMaxLength(256); // Joined performers
        modelBuilder.Entity<MediaFile>()
            .Property(m => m.Composers)
            .HasMaxLength(256); // Joined composers
        modelBuilder.Entity<MediaFile>()
            .Property(m => m.Genres)
            .HasMaxLength(128); // Joined genres
        modelBuilder.Entity<MediaFile>()
            .Property(m => m.Copyright)
        .HasMaxLength(128); // Copyright notice
        modelBuilder.Entity<MediaFile>()
            .Property(m => m.Comment)
            .HasMaxLength(256); // Comment field

        // Playlist
        modelBuilder.Entity<Playlist>()
            .HasIndex(p => p.Name)
            .IsUnique();
        modelBuilder.Entity<Playlist>()
            .Property(p => p.Name)
            .HasMaxLength(100); // Playlist name
        modelBuilder.Entity<Playlist>()
            .Property(p => p.SelectedTrack)
            .HasMaxLength(36); // GUID
        modelBuilder.Entity<Playlist>()
            .Ignore(p => p.TrackIds);

        // PlaylistTrack
        modelBuilder.Entity<PlaylistTrack>()
            .HasKey(pt => new { pt.PlaylistId, pt.TrackId });
        modelBuilder.Entity<PlaylistTrack>()
            .Property(pt => pt.TrackId)
            .HasMaxLength(36); // GUID
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

        modelBuilder.Entity<Playlist>()
            .HasOne(p => p.SelectedTrackNavigation)
            .WithMany()
            .HasForeignKey(p => p.SelectedTrack)
            .OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<Playlist>()
            .Property(p => p.SelectedTrack)
            .IsRequired(false);

        modelBuilder.Entity<MetadataCache>()
            .HasKey(mc => mc.Path);
        modelBuilder.Entity<MetadataCache>()
            .Property(mc => mc.Path)
            .HasMaxLength(256); // File path
        modelBuilder.Entity<MetadataCache>()
            .Property(mc => mc.Metadata)
            .HasMaxLength(4096); // JSON metadata
        modelBuilder.Entity<MetadataCache>()
            .Property(mc => mc.LastModified)
            .IsRequired();
        modelBuilder.Entity<MetadataCache>()
            .Property(mc => mc.Metadata)
            .IsRequired();
    }
}