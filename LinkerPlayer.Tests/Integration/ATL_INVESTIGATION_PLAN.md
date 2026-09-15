## ATL Vorbis RATING Investigation Plan

### Problem Summary
When setting a rating to 2.2★ on Vorbis files (FLAC):
- **Expected**: 2.2★ writes as 0.44 (Vorbis 0–1 scale), reads back as 2.2★
- **Observed**: 0.4 is read back, which converts to 2.0★ (wrong)
- **Hypothesis**: Something is rounding or dividing the RATING value incorrectly

### Key Questions to Answer

1. **Does ATL persist `AdditionalFields["RATING"]` in Vorbis?**
   - We write `track.AdditionalFields["RATING"] = "0.44"`
   - After `SaveAsync()`, does the file actually contain this RATING field?
   - Or does ATL convert it to Popularity (0–1 scale)?

2. **What scale does ATL use for Vorbis Popularity?**
   - Our assumption: 0–1 (fractional)
   - If we set `track.Popularity = 0.44f`, does it persist as 0.44 or round to 0 or 1?
   - Does ATL's Popularity setter accept 0–1 or expect 0–100 or 0–255?

3. **Does ATL strip the RATING field from AdditionalFields when reading?**
   - After fresh read, is `AdditionalFields["RATING"]` still there?
   - Or does ATL map it to Popularity and remove it from AdditionalFields?

4. **Where does 0.4 come from?**
   - Reading 0.44 as 0.4 suggests rounding to 1 decimal: `Math.Round(0.44, 1)` = 0.4 ✓
   - This means ATL's Popularity is returning 0.4 instead of 0.44
   - **Possible cause**: ATL stores the rating as an integer (0–255), so:
     - We write RATING="0.44"
     - ATL converts to internal representation as integer: `0.44 * 255 ≈ 112` (oops, wrong scale!)
     - ATL reads it back as `112 / 255 ≈ 0.439`, then rounds to 1 decimal for some reason

### Investigation Steps

#### Step 1: Direct File Inspection
After running the test that writes RATING, manually inspect the FLAC file using a hex editor or metadata viewer:
- Open the temp file with VorbisComment viewer (e.g., MusicBrainz Picard)
- Check if RATING field is present in the comments
- Note the exact byte sequence

#### Step 2: ATL Source Code Review
Look at `z440.atl.core` (v7.13.0) source:
- `VorbisTag.cs` or similar: how does it read/write RATING?
- `Track.cs`: does the Popularity property have special Vorbis handling?
- Check if there's any scale conversion in the property getter/setter

#### Step 3: Unit Test with Controlled Inputs
The `AtlVorbisRatingInvestigation` tests above are designed to:
- Write a known value, save, and re-read
- Log what ATL returns at each step
- Cross-check against expected scales

#### Step 4: NuGet Package Inspection
1. Download the ATL source or symbols from NuGet
2. Search for "Popularity" and "RATING" in the codebase
3. Look for any rounding or scale conversion in Vorbis handling

### Possible Outcomes & Fixes

**Outcome A: ATL strips RATING and forces Popularity**
- Fix: Never use AdditionalFields["RATING"]. Always use Popularity.
- Vorbis Popularity is 0–1, but ATL's Vorbis Popularity property may have issues.
- Solution: Use a different additional field (e.g., "MP_RATING" or "USER_RATING") that ATL doesn't touch.

**Outcome B: ATL persists RATING but interprets it as 0–255**
- Fix: Write RATING as "00.84" (with zero-padding) or store on a different scale.
- Or write as integer 0–100: (0.84 * 100) = 84

**Outcome C: Our read logic is wrong**
- Fix: Adjust PopularityToStars() or the read path in UpdateFromFileMetadata()

### Diagnostic Code Added
`LinkerPlayer.Tests\Integration\AtlVorbisRatingInvestigation.cs` contains:
1. `VorbisRatingWriteRead_CheckPersistence()` - Direct RATING write/read
2. `RoundTripConversion_Detect2Point2Bug()` - Math verification
3. `VorbisPopularitySetter_CheckActualScale()` - Popularity property behavior
4. `TwoPointTwoStarWrite_CheckWhichPersists()` - Full scenario reproduction
5. `AtlRatingScaleHypothesis()` - Rule out hypotheses

### How to Run
1. Place a test FLAC file in `LinkerPlayer.Tests\Integration\TestAudio\test.flac`
2. Run tests and examine console output
3. Check temp files with an external metadata viewer
4. Correlate ATL behavior with hypothesis

### References
- ATL Package: `z440.atl.core` v7.13.0
- Vorbis Comment Spec: https://xiph.org/flac/format.html#metadata_block_vorbis_comment
- ID3v2 POPM: 0–255 scale (clear)
- Vorbis custom comment: 0–1 scale (typical for RATING in spec)

---

**Next Action**: Run the diagnostic tests with console output visible, then examine the findings.
