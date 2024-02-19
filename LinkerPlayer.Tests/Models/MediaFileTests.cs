using FluentAssertions;
using LinkerPlayer.Models;

namespace LinkerPlayer.Tests.Models;

public class MediaFileTests
{
    private string fileName = "D:\\Music\\Patriotic\\Grand Funk\\Grand Funk - We're an American Band.mp3";

    [Fact]
    public void GivenArtistIsNull_ShouldUsePerformersValue()
    {
        MediaFile mediaFile = new MediaFile(fileName);

        mediaFile.UpdateFromFileMetadata(false);

        mediaFile.Performers.Should().Be("Grand Funk");
        mediaFile.Artists.Should().Be("Grand Funk");
    }
}