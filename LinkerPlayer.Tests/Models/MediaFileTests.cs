using LinkerPlayer.Models;
using Shouldly;

namespace LinkerPlayer.Tests.Models;

public class MediaFileTests
{
    private string fileName = "D:\\Music\\Patriotic\\Grand Funk\\Grand Funk - We're an American Band.mp3";

    [Fact]
    public void GivenArtistIsNull_ShouldUsePerformersValue()
    {
        MediaFile mediaFile = new MediaFile(fileName);

        mediaFile.UpdateFromFileMetadata(false);

        mediaFile.Performers.ShouldBe("Grand Funk");
        mediaFile.Artists.ShouldBe("Grand Funk");
    }
}