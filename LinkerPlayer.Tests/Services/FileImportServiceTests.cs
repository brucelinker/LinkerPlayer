using FluentAssertions;
using LinkerPlayer.Core;
using LinkerPlayer.Models;
using LinkerPlayer.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System.IO;
using Xunit;

namespace LinkerPlayer.Tests.Services;

public class FileImportServiceTests : IDisposable
{
    private readonly Mock<MusicLibrary> _mockMusicLibrary;
    private readonly Mock<ILogger<FileImportService>> _mockLogger;
    private readonly FileImportService _fileImportService;
    private readonly string _testDirectory;

    public FileImportServiceTests()
    {
        _mockMusicLibrary = new Mock<MusicLibrary>(Mock.Of<ILogger<MusicLibrary>>());
        _mockLogger = new Mock<ILogger<FileImportService>>();
        _fileImportService = new FileImportService(_mockMusicLibrary.Object, _mockLogger.Object);
        
        // Create a temporary test directory
        _testDirectory = Path.Combine(Path.GetTempPath(), "FileImportServiceTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    [Theory]
    [InlineData(".mp3", true)]
    [InlineData(".flac", true)]
    [InlineData(".wma", true)]
    [InlineData(".ape", true)]
    [InlineData(".wav", true)]
    [InlineData(".txt", false)]
    [InlineData(".jpg", false)]
    [InlineData("", false)]
    public void IsAudioFile_ShouldReturnCorrectResult(string extension, bool expected)
    {
        // Arrange
        var filePath = $"test{extension}";

        // Act
        var result = _fileImportService.IsAudioFile(filePath);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void IsAudioFile_WithNullOrEmptyPath_ShouldReturnFalse()
    {
        // Act & Assert
        _fileImportService.IsAudioFile(null!).Should().BeFalse();
        _fileImportService.IsAudioFile("").Should().BeFalse();
        _fileImportService.IsAudioFile("   ").Should().BeFalse();
    }

    [Fact]
    public void GetAudioFileCount_WithNonExistentFolder_ShouldReturnZero()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_testDirectory, "NonExistent");

        // Act
        var result = _fileImportService.GetAudioFileCount(nonExistentPath);

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public void GetAudioFileCount_WithMixedFiles_ShouldReturnOnlyAudioFiles()
    {
        // Arrange
        CreateTestFile("song1.mp3");
        CreateTestFile("song2.flac");
        CreateTestFile("document.txt");
        CreateTestFile("image.jpg");

        // Act
        var result = _fileImportService.GetAudioFileCount(_testDirectory);

        // Assert
        result.Should().Be(2);
    }

    [Fact]
    public async Task ImportFileAsync_WithValidAudioFile_ShouldReturnMediaFile()
    {
        // Arrange
        var testFile = CreateTestFile("test.mp3");
        var expectedMediaFile = new MediaFile { Path = testFile, Title = "Test Song" };
        
        _mockMusicLibrary.Setup(x => x.IsTrackInLibrary(It.IsAny<MediaFile>()))
                        .Returns((MediaFile?)null);
        
        _mockMusicLibrary.Setup(x => x.AddTrackToLibraryAsync(It.IsAny<MediaFile>(), true, false))
                        .ReturnsAsync(expectedMediaFile);

        // Act
        var result = await _fileImportService.ImportFileAsync(testFile);

        // Assert
        result.Should().NotBeNull();
        result!.Path.Should().Be(testFile);
        
        _mockMusicLibrary.Verify(x => x.AddTrackToLibraryAsync(It.IsAny<MediaFile>(), true, false), Times.Once);
    }

    [Fact]
    public async Task ImportFileAsync_WithExistingTrack_ShouldReturnClonedTrack()
    {
        // Arrange
        var testFile = CreateTestFile("existing.mp3");
        var existingTrack = new MediaFile { Path = testFile, Title = "Existing Song" };
        
        _mockMusicLibrary.Setup(x => x.IsTrackInLibrary(It.IsAny<MediaFile>()))
                        .Returns(existingTrack);

        // Act
        var result = await _fileImportService.ImportFileAsync(testFile);

        // Assert
        result.Should().NotBeNull();
        result.Should().NotBeSameAs(existingTrack); // Should be a clone
        
        _mockMusicLibrary.Verify(x => x.AddTrackToLibraryAsync(It.IsAny<MediaFile>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task ImportFileAsync_WithNonExistentFile_ShouldReturnNull()
    {
        // Arrange
        var nonExistentFile = Path.Combine(_testDirectory, "nonexistent.mp3");

        // Act
        var result = await _fileImportService.ImportFileAsync(nonExistentFile);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ImportFolderAsync_WithValidFolder_ShouldImportAllAudioFiles()
    {
        // Arrange
        CreateTestFile("song1.mp3");
        CreateTestFile("song2.flac");
        CreateTestFile("document.txt"); // Should be ignored
        
        var mediaFile1 = new MediaFile { Path = Path.Combine(_testDirectory, "song1.mp3") };
        var mediaFile2 = new MediaFile { Path = Path.Combine(_testDirectory, "song2.flac") };
        
        _mockMusicLibrary.Setup(x => x.IsTrackInLibrary(It.IsAny<MediaFile>()))
                        .Returns((MediaFile?)null);
        
        _mockMusicLibrary.SetupSequence(x => x.AddTrackToLibraryAsync(It.IsAny<MediaFile>(), false, false))
                        .ReturnsAsync(mediaFile1)
                        .ReturnsAsync(mediaFile2);

        // Act
        var result = await _fileImportService.ImportFolderAsync(_testDirectory);

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(x => x.Path.EndsWith("song1.mp3"));
        result.Should().Contain(x => x.Path.EndsWith("song2.flac"));
        
        _mockMusicLibrary.Verify(x => x.AddTrackToLibraryAsync(It.IsAny<MediaFile>(), false, false), Times.Exactly(2));
    }

    [Fact]
    public async Task ImportFilesAsync_WithMixedItems_ShouldImportCorrectly()
    {
        // Arrange
        var audioFile = CreateTestFile("audio.mp3");
        var textFile = CreateTestFile("text.txt");
        var subDir = Path.Combine(_testDirectory, "SubDir");
        Directory.CreateDirectory(subDir);
        CreateTestFile("sub.flac", subDir);
        
        var mediaFile1 = new MediaFile { Path = audioFile };
        var mediaFile2 = new MediaFile { Path = Path.Combine(subDir, "sub.flac") };
        
        _mockMusicLibrary.Setup(x => x.IsTrackInLibrary(It.IsAny<MediaFile>()))
                        .Returns((MediaFile?)null);
        
        _mockMusicLibrary.SetupSequence(x => x.AddTrackToLibraryAsync(It.IsAny<MediaFile>(), false, false))
                        .ReturnsAsync(mediaFile1)
                        .ReturnsAsync(mediaFile2);

        // Act
        var result = await _fileImportService.ImportFilesAsync(new[] { audioFile, textFile, subDir });

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(x => x.Path == audioFile);
        result.Should().Contain(x => x.Path.EndsWith("sub.flac"));
    }

    private string CreateTestFile(string fileName, string? directory = null)
    {
        var targetDir = directory ?? _testDirectory;
        var filePath = Path.Combine(targetDir, fileName);
        File.WriteAllText(filePath, "test content");
        return filePath;
    }
}