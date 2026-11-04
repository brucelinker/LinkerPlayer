using LinkerPlayer.Models;

namespace LinkerPlayer.Tests.Integration;

/// <summary>
/// Reproduces a FLAC whose stored Vorbis RATING overrides a fractional star value written by the app.
/// Set LINKERPLAYER_TEST_FLAC to a local FLAC file to run; the file is copied to temp and never modified.
/// </summary>
public class FlacRatingPersistenceTests
{
    private const string SourceEnvironmentVariable = "LINKERPLAYER_TEST_FLAC";

    [Fact]
    public async Task Rating_WithStaleVorbisRatingTag_ReloadsWrittenFractionalValue()
    {
        string? source = Environment.GetEnvironmentVariable(SourceEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
            return;

        string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".flac");
        File.Copy(source, tempFile, overwrite: true);

        try
        {
            MediaFile written = new(tempFile);
            written.UpdateFromFileMetadata(raisePropertyChanged: false);
            written.EnableDirtyTracking();
            written.Rating = 4.2;
            await written.PersistRatingToFileAsync(4.2);
            await Task.Delay(TimeSpan.FromSeconds(1));

            MediaFile reloaded = new(tempFile);
            reloaded.UpdateFromFileMetadata(raisePropertyChanged: false);

            Assert.Equal(4.2, reloaded.Rating, precision: 1);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
