﻿using LinkerPlayer.Models;
using Shouldly;

namespace LinkerPlayer.Tests.Models;

public class MediaFileTests
{
    private readonly string _fileName = "D:\\Music\\Patriotic\\Grand Funk\\Grand Funk - We're an American Band.mp3";

    [Fact]
    public void GivenArtistIsNull_ShouldUsePerformersValue()
    {
        MediaFile mediaFile = new(_fileName);

        mediaFile.UpdateFromFileMetadata(false);

        mediaFile.Performers.ShouldBe("Grand Funk");
        mediaFile.Artist.ShouldBe("Grand Funk");
    }
}