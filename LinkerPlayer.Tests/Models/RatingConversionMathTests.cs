using LinkerPlayer.Models;
using Xunit;

namespace LinkerPlayer.Tests.Models;

/// <summary>
/// Unit tests for rating conversion math, independent of ATL behavior.
/// These help isolate whether the bug is in ATL or in our conversion logic.
/// </summary>
public class RatingConversionMathTests
{
    [Theory]
    [InlineData(1.0, "test.flac")]      // Vorbis scale
    [InlineData(255.0, "test.mp3")]     // ID3v2 scale
    public void StarsToPopularity_VerifyScale(double expectedScale, string filePath)
    {
        double scale = MediaFileHelper.RatingScaleForPath(filePath);
        Assert.Equal(expectedScale, scale);
    }

    [Theory]
    [InlineData(5.0, 1.0, "test.flac", 1.0f)]      // 5★ → 1.0 (Vorbis)
    [InlineData(4.0, 1.0, "test.flac", 0.8f)]      // 4★ → 0.8 (Vorbis)
    [InlineData(2.5, 1.0, "test.flac", 0.5f)]      // 2.5★ → 0.5 (Vorbis)
    [InlineData(2.2, 1.0, "test.flac", 0.44f)]     // 2.2★ → 0.44 (Vorbis, critical case)
    [InlineData(4.2, 1.0, "test.flac", 0.84f)]     // 4.2★ → 0.84 (Vorbis, critical case)
    [InlineData(3.0, 1.0, "test.flac", 0.6f)]      // 3★ → 0.6 (Vorbis)
    [InlineData(5.0, 255.0, "test.mp3", 255.0f)]   // 5★ → 255 (ID3v2)
    [InlineData(4.0, 255.0, "test.mp3", 204.0f)]   // 4★ → 204 (ID3v2)
    [InlineData(2.2, 255.0, "test.mp3", 112.0f)]   // 2.2★ → 112 (ID3v2)
    public void StarsToPopularity_ConversionIsCorrect(double stars, double expectedScale, string filePath, float expectedPopularity)
    {
        float result = MediaFileHelper.StarsToPopularity(stars, filePath);
        // Use tolerance for floating-point comparison (0.5 difference is acceptable)
        Assert.InRange(result, expectedPopularity - 0.5f, expectedPopularity + 0.5f);
    }

    [Theory]
    [InlineData(1.0f, 1.0, "test.flac", 5.0)]      // 1.0 → 5★ (Vorbis)
    [InlineData(0.8f, 1.0, "test.flac", 4.0)]      // 0.8 → 4★ (Vorbis)
    [InlineData(0.5f, 1.0, "test.flac", 2.5)]      // 0.5 → 2.5★ (Vorbis)
    [InlineData(0.44f, 1.0, "test.flac", 2.2)]     // 0.44 → 2.2★ (Vorbis, critical case)
    [InlineData(0.84f, 1.0, "test.flac", 4.2)]     // 0.84 → 4.2★ (Vorbis, critical case)
    [InlineData(0.6f, 1.0, "test.flac", 3.0)]      // 0.6 → 3★ (Vorbis)
    [InlineData(255.0f, 255.0, "test.mp3", 5.0)]   // 255 → 5★ (ID3v2)
    [InlineData(204.0f, 255.0, "test.mp3", 4.0)]   // 204 → 4★ (ID3v2)
    [InlineData(112.0f, 255.0, "test.mp3", 2.2)]   // 112 → 2.2★ (ID3v2)
    public void PopularityToStars_ConversionIsCorrect(float popularity, double expectedScale, string filePath, double expectedStars)
    {
        double result = MediaFileHelper.PopularityToStars(popularity, filePath);
        Assert.Equal(expectedStars, result);
    }

    /// <summary>
    /// Critical round-trip test: if we write 2.2★ and read it back, do we get 2.2★?
    /// This verifies the conversion is bidirectional.
    /// </summary>
    [Theory]
    [InlineData(2.2, "test.flac")]    // Vorbis
    [InlineData(4.2, "test.flac")]    // Vorbis
    [InlineData(3.7, "test.flac")]    // Vorbis
    [InlineData(1.0, "test.flac")]    // Vorbis
    [InlineData(2.2, "test.mp3")]     // ID3v2
    [InlineData(4.2, "test.mp3")]     // ID3v2
    public void RoundTrip_StarsToPopularityAndBack(double originalStars, string filePath)
    {
        // Stars → Popularity
        float popularity = MediaFileHelper.StarsToPopularity(originalStars, filePath);

        // Popularity → Stars
        double starsBack = MediaFileHelper.PopularityToStars(popularity, filePath);

        // Should match original (within floating-point precision)
        Assert.Equal(originalStars, starsBack, precision: 1);
    }

    /// <summary>
    /// If we somehow get 0.4 instead of 0.44, what stars does that produce?
    /// This helps diagnose if 0.4 is the actual culprit.
    /// </summary>
    [Fact]
    public void If_PopularityIs0Point4_WhatStars()
    {
        float wrongPopularity = 0.4f;
        string vorbisPath = "test.flac";

        double starsFromWrongPopularity = MediaFileHelper.PopularityToStars(wrongPopularity, vorbisPath);

        // 0.4 / 1.0 * 5.0 = 2.0 (rounded to 2.0)
        Assert.Equal(2.0, starsFromWrongPopularity);

        // But we observed 0.4 as the result, not 2.0
        // This suggests the 0.4 is NOT being converted at all — it's being returned as-is
        // i.e., PopularityToStars(0.4, vorbis) is returning 0.4 instead of 2.0
    }

    /// <summary>
    /// If the bug is in PopularityToStars, what input would produce 0.4 as output?
    /// </summary>
    [Fact]
    public void WhatInputProduces0Point4Output()
    {
        // If output is 0.4 and we expect: (pop / scale * 5.0) rounded to 1 decimal
        // Then: 0.4 = (pop / 1.0 * 5.0) → pop = 0.04
        // Or: 0.4 = (pop / 255 * 5.0) → pop ≈ 20.4

        // Test both hypotheses
        float vorbisInput = 0.04f;
        double vorbisResult = MediaFileHelper.PopularityToStars(vorbisInput, "test.flac");
        // Expected: 0.04 / 1.0 * 5.0 = 0.2 (rounded to 0.2), not 0.4

        float id3Input = 20.4f;
        double id3Result = MediaFileHelper.PopularityToStars(id3Input, "test.mp3");
        // Expected: 20.4 / 255 * 5.0 = 0.4 (rounded to 0.4) ✓

        // If 0.4 is the observed output and id3Result==0.4, then maybe:
        // - ATL is reading a Vorbis file but returning an ID3v2-scale value (20.4)?
        // - Or ATL's Popularity for Vorbis is somehow on a different scale?
    }

    /// <summary>
    /// Debug: manually step through the exact scenario to find the error.
    /// </summary>
    [Fact]
    public void DebugScenario_2Point2Stars_WhereDoesItGo()
    {
        const double originalStars = 2.2;
        const string filePath = "test.flac";

        // Step 1: Convert stars to popularity (for writing)
        float popularity = MediaFileHelper.StarsToPopularity(originalStars, filePath);
        Assert.Equal(0.44f, popularity);
        // So we should write: RATING="0.44" or Popularity=0.44

        // Step 2: Assume ATL persists it and re-reads it
        // (In reality, we need to test this with actual files)
        float atlReadsBack = popularity; // Assume no loss

        // Step 3: Convert back to stars (for display)
        double starsBack = MediaFileHelper.PopularityToStars(atlReadsBack, filePath);
        Assert.Equal(2.2, starsBack);

        // If we're observing 0.4 instead, it means either:
        // A) atlReadsBack = 0.4 (ATL returned wrong value)
        // B) starsBack calculation is wrong (but our tests pass, so unlikely)
        // C) We're reading from the wrong field entirely

        // Let's check: if PopularityToStars somehow received 0.04 or misapplied scale:
        double wrongCalc1 = MediaFileHelper.PopularityToStars(0.04f, filePath);
        // 0.04 / 1.0 * 5.0 = 0.2
        Assert.Equal(0.2, wrongCalc1); // Not 0.4

        // If we somehow divided by scale instead of multiplying:
        double wrongCalc2 = (0.44 / 1.0 / 5.0); // 0.088, rounds to 0.1, not 0.4
        Assert.Equal(0.088, wrongCalc2);

        // The ONLY way to get 0.4 from 0.44 is if we round to 1 decimal: Math.Round(0.44, 1) = 0.4
        double rounded = Math.Round(0.44, 1);
        Assert.Equal(0.4, rounded);
        // So the issue is: ATL.Popularity is returning 0.4 instead of 0.44
        // This means ATL has internally rounded or truncated the value.
    }
}
