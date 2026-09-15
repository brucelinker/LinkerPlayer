using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ATL;
using Xunit;

namespace LinkerPlayer.Tests.Integration;

/// <summary>
/// Investigation of ATL's Vorbis RATING handling to understand:
/// 1. Whether ATL persists AdditionalFields["RATING"] when SaveAsync() is called
/// 2. Whether ATL maps Vorbis RATING to Popularity (removing it from AdditionalFields)
/// 3. The actual value scales used by ATL for different formats
/// 4. Why 2.2★ reads back as 0.4 (double division hypothesis)
/// </summary>
public class AtlVorbisRatingInvestigation
{
    private static string GetTestAudioPath(string filename) =>
        Path.Combine(AppContext.BaseDirectory, "Integration", "TestAudio", filename);

    /// <summary>
    /// Write a raw RATING value to Vorbis additional fields and read it back
    /// to see if ATL persists it and whether it appears in Popularity or AdditionalFields.
    /// </summary>
    [Fact]
    public void VorbisRatingWriteRead_CheckPersistence()
    {
        string testFile = GetTestAudioPath("test.flac");
        if (!File.Exists(testFile))
        {
            // Skip if test audio not available
            return;
        }

        // Create a copy to avoid modifying the original
        string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".flac");
        File.Copy(testFile, tempFile, overwrite: true);

        try
        {
            // Step 1: Write a Vorbis RATING value directly
            {
                Track track = new(tempFile);
                string ratingValue = "0.84"; // Represents 4.2 stars (4.2 * 1.0 / 5.0 = 0.84)

                string? ratingBefore = track.AdditionalFields?.TryGetValue("RATING", out var v) ?? false ? v : null;
                Console.WriteLine($"[WRITE] Before: Popularity={track.Popularity}, RATING in AdditionalFields={ratingBefore ?? "N/A"}");

                track.AdditionalFields["RATING"] = ratingValue;
                track.Popularity = null; // Ensure we're only testing RATING field

                bool saved = track.SaveAsync(writeProgress: null).Result;
                Console.WriteLine($"[WRITE] SaveAsync returned: {saved}");
                string? ratingAfter = track.AdditionalFields?.TryGetValue("RATING", out var v2) ?? false ? v2 : null;
                Console.WriteLine($"[WRITE] After write (in-memory): Popularity={track.Popularity}, RATING={ratingAfter ?? "N/A"}");
            }

            // Step 2: Re-read the file fresh
            {
                Track track = new(tempFile);
                string? ratingRead = track.AdditionalFields?.TryGetValue("RATING", out var v) ?? false ? v : null;
                Console.WriteLine($"[READ] Fresh read: Popularity={track.Popularity}, RATING in AdditionalFields={ratingRead ?? "N/A"}");

                // Also check all RATING-like fields
                if (track.AdditionalFields != null)
                {
                    var ratingFields = track.AdditionalFields
                        .Where(kv => kv.Key.Contains("RATING", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    Console.WriteLine($"[READ] All RATING fields: {string.Join(", ", ratingFields.Select(kv => $"{kv.Key}={kv.Value}"))}");
                }
            }
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// Test the PopularityToStars and StarsToPopularity round-trip to catch the double-division bug.
    /// 2.2★ → 0.44 (Vorbis, 0–1 scale) should round to 0.44, not 0.4.
    /// </summary>
    [Fact]
    public void RoundTripConversion_Detect2Point2Bug()
    {
        // Vorbis scale (0–1)
        string flacPath = "test.flac";
        double stars = 2.2;

        // Simulate what StarsToPopularity does
        double scale = 1.0; // Vorbis
        double clampedStars = Math.Clamp(stars, 0.0, 5.0); // 2.2
        double value = clampedStars * scale / 5.0; // 2.2 * 1.0 / 5.0 = 0.44
        int digits = scale <= 1.0 ? 2 : 1;
        float popularity = (float)Math.Round(value, digits); // Should be 0.44

        Console.WriteLine($"[CONVERSION] {stars}★ on Vorbis scale (1.0):");
        Console.WriteLine($"  Clamped: {clampedStars}");
        Console.WriteLine($"  Raw calculation: {value}");
        Console.WriteLine($"  Rounded ({digits} digits): {popularity}");

        Assert.Equal(0.44f, popularity); // This should NOT be 0.4

        // Now reverse it
        double starsBack = Math.Round(Math.Clamp(popularity / scale * 5.0, 0.0, 5.0), 1);
        Console.WriteLine($"[CONVERSION] Back to stars: {starsBack}");
        Assert.Equal(2.2, starsBack);
    }

    /// <summary>
    /// Check if ATL's Popularity setter for Vorbis files actually uses 0–1 scale or something else.
    /// This will help confirm whether we should write RATING directly or use Popularity.
    /// </summary>
    [Fact]
    public void VorbisPopularitySetter_CheckActualScale()
    {
        string testFile = GetTestAudioPath("test.flac");
        if (!File.Exists(testFile))
        {
            return;
        }

        string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".flac");
        File.Copy(testFile, tempFile, overwrite: true);

        try
        {
            // Write Popularity via ATL's property
            {
                Track track = new(tempFile);
                Console.WriteLine($"[POPULARITY-WRITE] Before: Popularity={track.Popularity}");

                track.Popularity = 0.84f; // Try the 0–1 scale
                bool saved = track.SaveAsync(writeProgress: null).Result;
                Console.WriteLine($"[POPULARITY-WRITE] Set Popularity=0.84, SaveAsync={saved}");
            }

            // Read back
            {
                Track track = new(tempFile);
                Console.WriteLine($"[POPULARITY-READ] After re-read: Popularity={track.Popularity}");
                string? ratingField = track.AdditionalFields?.TryGetValue("RATING", out var v) ?? false ? v : null;
                Console.WriteLine($"[POPULARITY-READ] RATING field: {ratingField ?? "N/A"}");

                // If Popularity rounds to a whole number, that's a clue ATL expects 0–255
                if (track.Popularity.HasValue && track.Popularity % 1 == 0)
                {
                    Console.WriteLine("[POPULARITY-READ] *** Popularity is whole number — likely 0–255 scale! ***");
                }
            }
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// Simulate the exact scenario: write 2.2★ as both Popularity and RATING, see which one persists.
    /// </summary>
    [Fact]
    public void TwoPointTwoStarWrite_CheckWhichPersists()
    {
        string testFile = GetTestAudioPath("test.flac");
        if (!File.Exists(testFile))
        {
            return;
        }

        string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".flac");
        File.Copy(testFile, tempFile, overwrite: true);

        try
        {
            // Write 2.2★ using our current logic
            {
                Track track = new(tempFile);
                double stars = 2.2;
                double scale = 1.0; // Vorbis
                double value = Math.Round(stars * scale / 5.0, 2); // 0.44

                Console.WriteLine($"[2.2STAR-WRITE] Writing 2.2★:");
                Console.WriteLine($"  RATING={value.ToString("0.##")}");
                Console.WriteLine($"  Popularity (before)={track.Popularity}");

                track.AdditionalFields["RATING"] = value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                // NOT setting Popularity
                bool saved = track.SaveAsync(writeProgress: null).Result;
                Console.WriteLine($"[2.2STAR-WRITE] SaveAsync={saved}");
            }

            // Read back and check the calculated stars
            {
                Track track = new(tempFile);
                string? ratingField = track.AdditionalFields?.TryGetValue("RATING", out var v) ?? false ? v : null;
                Console.WriteLine($"[2.2STAR-READ] After fresh read:");
                Console.WriteLine($"  Popularity={track.Popularity}");
                Console.WriteLine($"  RATING={ratingField ?? "N/A"}");

                // Simulate the read logic from MediaFile.UpdateFromFileMetadata
                float? rawRating = null;
                if (track.AdditionalFields != null && track.AdditionalFields.TryGetValue("RATING", out string? r))
                {
                    if (double.TryParse(r, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out double raw) && raw > 0)
                    {
                        rawRating = (float)(raw <= 1.0 ? raw : raw / 100.0);
                    }
                }
                rawRating ??= track.Popularity;

                double calculatedStars = 0;
                if (rawRating.HasValue && rawRating.Value > 0)
                {
                    double scale = 1.0; // Vorbis
                    calculatedStars = Math.Round(Math.Clamp(rawRating.Value / scale * 5.0, 0.0, 5.0), 1);
                }

                Console.WriteLine($"[2.2STAR-READ] Calculated stars from read: {calculatedStars}");
                Assert.Equal(2.2, calculatedStars); // This is failing with 0.4?
            }
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// Check the hypothesis: is ATL reading the RATING field but returning it in the wrong scale?
    /// E.g., reading "0.44" and somehow treating it as 0–255, then rounding to 0 or 1, then us dividing by 5?
    /// </summary>
    [Fact]
    public void AtlRatingScaleHypothesis()
    {
        // IF ATL is reading "0.44" and treating it as 0–255:
        //   0.44 on 0–255 scale → 0.44 / 255 * 5 = 0.00863... → rounds to 0
        // IF ATL is reading "0.44" and dividing by 5:
        //   0.44 / 5 = 0.088 → rounds to 0.1, not 0.4
        // IF we're reading 0.4 (ATL's Popularity) and scale is wrong:
        //   0.4 * 1.0 / 5.0 * 5.0 = 0.4 ✓ (but wrong calculation path)
        //   0.4 * 255 / 5.0 = 20.4 (wrong scale entirely)

        // The 0.4 result suggests:
        //   ATL.Popularity = 0.4 (ATL reads "0.44" and rounds to nearest 0.1?)
        //   OR ATL.Popularity = 0.4 because it's (0.44 * 100) / 255 ≈ 0.173? No.
        //   OR we're dividing Popularity (0–1) by scale (1.0) and getting 0.4... which is wrong.

        Console.WriteLine("[HYPOTHESIS] If 2.2★ → 0.44 is written, but 0.4 is read:");
        Console.WriteLine("  Scenario A: ATL Popularity returns 0.4 (rounded 0.44?)");
        Console.WriteLine("  Scenario B: Our read logic divides something wrong");

        // Test: if Popularity=0.4 and scale=1.0:
        float popularity = 0.4f;
        double scale = 1.0;
        double stars = Math.Round(Math.Clamp(popularity / scale * 5.0, 0.0, 5.0), 1);
        Console.WriteLine($"  If Popularity=0.4, scale=1.0 → stars={stars}");
        Assert.Equal(2.0, stars); // Not 2.2!

        // If scale was wrongly applied as (popularity / 5.0) instead of (popularity * 5.0 / scale):
        double wrongCalc = Math.Round(Math.Clamp(0.4 / 5.0, 0.0, 5.0), 1);
        Console.WriteLine($"  If (0.4 / 5.0) → stars={wrongCalc}");
        Assert.Equal(0.1, wrongCalc); // Still not 0.4

        // The only way to get exactly 0.4 is if we read 0.4 and DON'T convert:
        Console.WriteLine($"  Direct read without conversion: 0.4");
    }
}
