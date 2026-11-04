using LinkerPlayer.Models;

namespace LinkerPlayer.Tests.Models;

public class QueryParserRatingTests
{
    [Theory]
    [InlineData("rating >= 4.5")]
    [InlineData("stars >= 4.5")]
    public void TryParse_RatingComparison_MapsRatingField(string query)
    {
        bool parsed = QueryParser.TryParse(query, out List<List<FilterCriteria>> orGroups);

        Assert.True(parsed);
        FilterCriteria criteria = Assert.Single(Assert.Single(orGroups));
        Assert.Equal(FilterType.Rating, criteria.Type);
        Assert.Equal(FilterOperator.GreaterThanOrEqual, criteria.Operator);
        Assert.Equal("4.5", criteria.Value);
    }

    [Fact]
    public void Matches_RatingComparison_SupportsFractionalValues()
    {
        bool parsed = QueryParser.TryParse("rating >= 4.5", out List<List<FilterCriteria>> orGroups);
        Assert.True(parsed);
        FilterCriteria criteria = Assert.Single(Assert.Single(orGroups));

        Assert.True(criteria.Matches(new MediaFile { Rating = 4.75 }));
        Assert.False(criteria.Matches(new MediaFile { Rating = 4.25 }));
    }

    [Fact]
    public void Matches_RatingEquals_TreatsRatingAsNumeric()
    {
        bool parsed = QueryParser.TryParse("rating = 4.50", out List<List<FilterCriteria>> orGroups);
        Assert.True(parsed);
        FilterCriteria criteria = Assert.Single(Assert.Single(orGroups));

        Assert.True(criteria.Matches(new MediaFile { Rating = 4.5 }));
        Assert.False(criteria.Matches(new MediaFile { Rating = 4.25 }));
    }

    [Fact]
    public void TryParse_RatingBounds_CreatesBetweenCriterion()
    {
        bool parsed = QueryParser.TryParse("rating >= 3 rating <= 4.5", out List<List<FilterCriteria>> orGroups);

        Assert.True(parsed);
        FilterCriteria criteria = Assert.Single(Assert.Single(orGroups));
        Assert.Equal(FilterType.Rating, criteria.Type);
        Assert.Equal(FilterOperator.Between, criteria.Operator);
        Assert.Equal("3", criteria.Value);
        Assert.Equal("4.5", criteria.ValueSecondary);
        Assert.True(criteria.Matches(new MediaFile { Rating = 4.25 }));
        Assert.False(criteria.Matches(new MediaFile { Rating = 4.75 }));
    }
}
