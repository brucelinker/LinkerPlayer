using Microsoft.EntityFrameworkCore;
using LinkerPlayer.Models;

namespace LinkerPlayer.Database;

public class MusicLibraryDbContext : DbContext
{
    public DbSet<MediaFile> Tracks { get; set; }
    public DbSet<Playlist> Playlists { get; set; }
    public DbSet<PlaylistTrack> PlaylistTracks { get; set; }
    public DbSet<MetadataCache> MetadataCache { get; set; }

    public MusicLibraryDbContext(DbContextOptions<MusicLibraryDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // MediaFile: Only Id and Path are mapped
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
        // Ignore all other properties
        modelBuilder.Entity<MediaFile>().Ignore(m => m.FileName);
        modelBuilder.Entity<MediaFile>().Ignore(m => m.Title);
        modelBuilder.Entity<MediaFile>().Ignore(m => m.Artist);
        modelBuilder.Entity<MediaFile>().Ignore(m => m.Album);
        modelBuilder.Entity<MediaFile>().Ignore(m => m.Performers);
        modelBuilder.Entity<MediaFile>().Ignore(m => m.Composers);
        modelBuilder.Entity<MediaFile>().Ignore(m => m.Genres);
        modelBuilder.Entity<MediaFile>().Ignore(m => m.Copyright);
        modelBuilder.Entity<MediaFile>().Ignore(m => m.Comment);
        modelBuilder.Entity<MediaFile>().Ignore(m => m.Track);
        modelBuilder.Entity<MediaFile>().Ignore(m => m.TrackCount);
        modelBuilder.Entity<MediaFile>().Ignore(m => m.Disc);
        modelBuilder.Entity<MediaFile>().Ignore(m => m.DiscCount);
        modelBuilder.Entity<MediaFile>().Ignore(m => m.Year);
        modelBuilder.Entity<MediaFile>().Ignore(m => m.Duration);
        modelBuilder.Entity<MediaFile>().Ignore(m => m.Bitrate);
        modelBuilder.Entity<MediaFile>().Ignore(m => m.SampleRate);
        modelBuilder.Entity<MediaFile>().Ignore(m => m.Channels);
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
            .Property(p => p.SelectedTrack)
            .HasMaxLength(36);
        modelBuilder.Entity<Playlist>()
            .Ignore(p => p.TrackIds);
        modelBuilder.Entity<Playlist>()
            .HasOne(p => p.SelectedTrackNavigation)
            .WithMany()
            .HasForeignKey(p => p.SelectedTrack)
            .OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<Playlist>()
            .Property(p => p.SelectedTrack)
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

        // MetadataCache (optional)
        modelBuilder.Entity<MetadataCache>()
            .HasKey(mc => mc.Path);
        modelBuilder.Entity<MetadataCache>()
            .Property(mc => mc.Path)
            .HasMaxLength(256);
        modelBuilder.Entity<MetadataCache>()
            .Property(mc => mc.Metadata)
            .HasMaxLength(4096);
        modelBuilder.Entity<MetadataCache>()
            .Property(mc => mc.LastModified)
            .IsRequired();
        modelBuilder.Entity<MetadataCache>()
            .Property(mc => mc.Metadata)
            .IsRequired();
    }
}