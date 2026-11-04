using LinkerPlayer.Models;

namespace LinkerPlayer.Tests.Models;

public class QueryParserMusicBrainzIdTests
{
    [Fact]
    public void TryParse_MbidEmptyString_MapsToMusicBrainzIdEqualsEmpty()
    {
        bool parsed = QueryParser.TryParse("mbid = \"\"", out List<List<FilterCriteria>> orGroups);

        Assert.True(parsed);
        FilterCriteria criteria = Assert.Single(Assert.Single(orGroups));
        Assert.Equal(FilterType.MusicBrainzId, criteria.Type);
        Assert.Equal(FilterOperator.Equals, criteria.Operator);
        Assert.Equal(string.Empty, criteria.Value);
    }

    [Fact]
    public void Matches_MbidEmptyString_MatchesTrackWithoutMusicBrainzId()
    {
        FilterCriteria criteria = new() { Type = FilterType.MusicBrainzId, Operator = FilterOperator.Equals, Value = string.Empty };
        MediaFile track = new() { MusicBrainzId = string.Empty };

        Assert.True(criteria.Matches(track));
    }

    [Fact]
    public void Matches_MbidEmptyString_DoesNotMatchTrackWithMusicBrainzId()
    {
        FilterCriteria criteria = new() { Type = FilterType.MusicBrainzId, Operator = FilterOperator.Equals, Value = string.Empty };
        MediaFile track = new() { MusicBrainzId = "5f2a1c3e-1111-2222-3333-444455556666" };

        Assert.False(criteria.Matches(track));
    }
}
