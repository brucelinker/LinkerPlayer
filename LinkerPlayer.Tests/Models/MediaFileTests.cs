namespace LinkerPlayer.Tests.Models;

public class MediaFileTests
{
    //[Fact]
    //public void MediaFile_Creation_ShouldSetBasicProperties()
    //{
    //    // Arrange
    //    var fileName = "test.mp3";

    //    // Act
    //    var mediaFile = new MediaFile
    //    {
    //        Path = fileName,
    //        FileName = fileName,
    //        Title = "Test Song",
    //        Artist = "Test Artist"
    //    };

    //    // Assert
    //    mediaFile.Path.ShouldBe(fileName);
    //    mediaFile.FileName.ShouldBe(fileName);
    //    mediaFile.Title.ShouldBe("Test Song");
    //    mediaFile.Artist.ShouldBe("Test Artist");
    //}

    //[Fact]
    //public void MediaFile_Clone_ShouldCreateCopy()
    //{
    //    // Arrange
    //    var original = new MediaFile
    //    {
    //        Id = "test-id",
    //        Path = "test.mp3",
    //        Title = "Original Title",
    //        Artist = "Original Artist",
    //        Album = "Original Album"
    //    };

    //    // Act
    //    var clone = original.Clone();

    //    // Assert
    //    clone.ShouldNotBeSameAs(original);
    //    clone.Id.ShouldBe(original.Id);
    //    clone.Path.ShouldBe(original.Path);
    //    clone.Title.ShouldBe(original.Title);
    //    clone.Artist.ShouldBe(original.Artist);
    //    clone.Album.ShouldBe(original.Album);
    //}

    //[Fact]
    //public void MediaFile_ToString_ShouldReturnFormattedString()
    //{
    //    // Arrange
    //    var mediaFile = new MediaFile
    //    {
    //        Artist = "Test Artist",
    //        Title = "Test Title"
    //    };

    //    // Act
    //    var result = mediaFile.ToString();

    //    // Assert
    //    result.ShouldContain("Test Artist");
    //    result.ShouldContain("Test Title");
    //}

    //[Fact]
    //public void MediaFile_InitialState_ShouldBeStopped()
    //{
    //    // Arrange & Act
    //    var mediaFile = new MediaFile();

    //    // Assert
    //    mediaFile.State.ShouldBe(ManagedBass.PlaybackState.Stopped);
    //}

    // NOTE: Tests involving UpdateFromFileMetadata are commented out because they require:
    // 1. Actual audio files with metadata
    // 2. TagLib libraries
    // 3. File system access
    // These would be better as integration tests

    /*
    [Fact]
    public void GivenArtistIsNull_ShouldUsePerformersValue()
    {
        // This test requires actual file metadata extraction
        // Better suited for integration testing
    }
    */
}
