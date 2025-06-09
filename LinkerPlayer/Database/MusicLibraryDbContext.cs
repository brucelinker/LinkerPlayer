using Microsoft.EntityFrameworkCore;
using LinkerPlayer.Models;

namespace LinkerPlayer.Database;

public class MusicLibraryDbContext : DbContext
{
    public DbSet<MediaFile> Tracks { get; set; }
    public DbSet<Playlist> Playlists { get; set; }
    public DbSet<PlaylistTrack> PlaylistTracks { get; set; }

    public MusicLibraryDbContext(DbContextOptions<MusicLibraryDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MediaFile>()
            .HasIndex(t => new { t.Path, t.Album, t.Duration })
            .IsUnique();

        modelBuilder.Entity<Playlist>()
            .HasIndex(p => p.Name)
            .IsUnique();

        modelBuilder.Entity<PlaylistTrack>()
            .HasKey(pt => new { pt.PlaylistId, pt.TrackId });

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

        // Explicitly configure SelectedTrack as nullable
        modelBuilder.Entity<Playlist>()
            .Property(p => p.SelectedTrack)
            .IsRequired(false);
    }
}