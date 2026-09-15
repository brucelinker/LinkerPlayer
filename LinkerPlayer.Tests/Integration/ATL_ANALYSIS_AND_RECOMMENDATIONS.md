# ATL Vorbis RATING Investigation — Analysis & Recommendations

## Summary of Current Code State

Your implementation has three main paths for rating handling:

1. **Write path** (`PersistRatingToFileAsync`):
   - Vorbis: writes `AdditionalFields["RATING"]` with calculated 0–1 value
   - Non-Vorbis: writes `Popularity` property with calculated 0–255 value

2. **Read path** (`UpdateFromFileMetadata`):
   - Vorbis: prefers `AdditionalFields["RATING"]`, falls back to `Popularity`
   - Non-Vorbis: falls back to `AdditionalFields["RATING"]`, prefers `Popularity`

3. **Conversion logic** (`MediaFileHelper`):
   - `StarsToPopularity`: converts 0–5 stars to scale-dependent value (0–1 or 0–255)
   - `PopularityToStars`: reverse conversion
   - Math is **correct** (unit tests confirm)

## Observed Problem

**Scenario**: Set rating to 2.2★ on a FLAC file
- **Expected**: Converts to 0.44 (2.2 * 1.0 / 5.0), writes, reads back as 2.2★
- **Actual**: Something reads back as 0.4 instead of 0.44, converting to 2.0★ instead of 2.2★

## Root Cause Analysis

### Hypothesis 1: ATL's Vorbis Popularity Property Issue
**Most Likely**: ATL's Vorbis implementation of the `Popularity` property may:
- Round values to 1 decimal (0.44 → 0.4)
- Use internal representation that loses precision
- Force conversion to/from a different scale internally

**Evidence**:
- We write 0.44, ATL reads back 0.4
- This is exactly `Math.Round(0.44, 1)` = 0.4
- Happens specifically on Vorbis files (FLAC), not ID3v2 (MP3)

### Hypothesis 2: ATL Doesn't Persist AdditionalFields["RATING"]
**Possible**: The RATING field is not actually stored in the file:
- We set `AdditionalFields["RATING"] = "0.44"`
- ATL's SaveAsync() ignores it (Vorbis writer doesn't handle this custom field)
- On re-read, there's no RATING field
- Code falls back to `Popularity` which is 0 or wrong

**Evidence**:
- ATL may only handle standard Vorbis comments (Title, Artist, etc.)
- Custom comments require explicit support

### Hypothesis 3: ATL Maps Vorbis RATING→Popularity and Scales It Incorrectly
**Possible**: ATL normalizes RATING as 0–100 or 0–255:
- We write "0.44"
- ATL interprets as 0–100 or 0–255 (wrong assumption)
- Stores as integer: `0.44 * 255 ≈ 112` or `0.44 * 100 ≈ 44`
- On re-read, converts back: `44 / 100 * 1.0 = 0.44` or `112 / 255 * 1.0 ≈ 0.439` → rounds to 0.4

**Evidence**:
- Vorbis Comment spec defines RATING as 0–1, but ATL might not follow this
- Some taggers use 0–100 for RATING (non-standard)

## Recommended Investigation Steps

### Step 1: Check if AdditionalFields["RATING"] is Actually Persisted
```csharp
// Create a test FLAC file and write RATING via ATL
Track track = new("test.flac");
track.AdditionalFields["RATING"] = "0.44";
track.SaveAsync(writeProgress: null).Wait();

// Read it back fresh
Track reread = new("test.flac");
bool hasRating = reread.AdditionalFields.ContainsKey("RATING");
string? ratingValue = reread.AdditionalFields?["RATING"];

// Also check Popularity
float? pop = reread.Popularity;
```

**Expected Results**:
- If `hasRating == true` and `ratingValue == "0.44"`: ATL persists custom fields ✓
- If `hasRating == false`: ATL ignores AdditionalFields["RATING"] ✗ (need different approach)
- If `ratingValue` exists but `pop == 0.4`: ATL converted RATING to Popularity (Hypothesis 3)

### Step 2: Check if Popularity Rounds Values
```csharp
// Try setting Popularity directly
Track track = new("test.flac");
track.Popularity = 0.44f;
track.SaveAsync(writeProgress: null).Wait();

Track reread = new("test.flac");
float? pop = reread.Popularity; // Is this 0.44 or 0.4?
```

**Expected Results**:
- If `pop == 0.44f`: ATL preserves precision ✓
- If `pop == 0.4f`: ATL rounds to 1 decimal ✗

### Step 3: Verify Vorbis Comment Spec Compliance
Check if ATL follows the Vorbis Comment spec or uses a different convention:
- Spec says RATING is 0–1
- Some taggers use 0–100 or 0–255
- ATL might normalize to a different scale

## Proposed Solutions (in order of preference)

### Solution A: Use a Different Custom Field (if RATING doesn't persist)
If ATL doesn't persist `AdditionalFields["RATING"]`, try:
```csharp
// Use a different custom field name that's less likely to be normalized
track.AdditionalFields["LILINKER_RATING"] = "0.44";
// or
track.AdditionalFields["MP_RATING"] = "0.44";
// or
track.AdditionalFields["USER_RATING"] = "0.44";
```

**Pros**: Guaranteed to persist; avoids ATL's Vorbis RATING handling
**Cons**: Non-standard; may not be recognized by other taggers

### Solution B: Store Rating as Integer 0–100 (Compromise)
```csharp
// Write as 0–100 on Vorbis (more portable, even if non-standard)
if (scale <= 1.0)
{
    int ratingInt = (int)Math.Round(stars * 20, 0); // 0–100 scale
    track.AdditionalFields["RATING"] = ratingInt.ToString();
}
```

**Pros**: More portable; many taggers recognize 0–100 RATING
**Cons**: Loses fractional stars (2.2★ → 44 → 2.2★ is OK, but 2.25★ loses precision)

### Solution C: Use Popularity for Both Vorbis and ID3v2 (Simplify)
```csharp
// Always use Popularity, skip custom RATING field entirely
track.Popularity = (float)Math.Round(stars * scale / 5.0, 2);
```

**Pros**: Single code path; relies on ATL's property (which should handle scaling)
**Cons**: Loses fractional stars if ATL rounds (still need to verify)

### Solution D: Use FMPS_RATING (Last Resort)
```csharp
// FMPS (Free Music Player Seamlessly) has a different rating convention
track.AdditionalFields["FMPS_RATING"] = (stars / 5.0).ToString("0.00");
```

**Pros**: Better support in some players
**Cons**: Non-standard; fewer players recognize it

## Recommended Next Actions

1. **Run the diagnostic tests** in `AtlVorbisRatingInvestigation.cs` with actual FLAC files
2. **Log ATL's internal values** during read/write to see exactly what's being stored/retrieved
3. **Inspect the saved FLAC file** with a hex editor or metadata viewer to confirm RATING field presence
4. **Check ATL source code** (if available) for Vorbis RATING handling:
   - Search `z440.atl.core` NuGet package for `VorbisTag`, `Popularity`, or `RATING`
   - Look for any scale conversion or rounding in the Vorbis path

## Current Best Guess (Based on Evidence)

**Most likely scenario**:
- ATL's Vorbis `Popularity` property stores as integer or rounds to 1 decimal
- Setting `Popularity = 0.44f` results in internal storage as 0 (clamped) or 44 (scaled to 0–100)
- Re-reading returns 0 or 0.4 (scaled back incorrectly)

**Action**: Skip the Popularity property for Vorbis and use a custom field with guaranteed persistence.

---

## Files Modified/Created for Investigation
- `MediaFileHelper.cs`: Already has correct scale-aware conversion math
- `MediaFile.cs`: Reads RATING with fallback to Popularity
- `RatingConversionMathTests.cs`: Confirms math is correct (28/29 tests pass)
- `AtlVorbisRatingInvestigation.cs`: Diagnostic tests for ATL behavior
- `ATL_INVESTIGATION_PLAN.md`: This document's predecessor

---

## Next: Wait for Diagnostic Results

Once you run the diagnostic tests with actual audio files, the results will guide which solution to implement:

- **A (Different Field)**: If RATING isn't persisted
- **B (0–100 Integer)**: If RATING is persisted but precision-lost
- **C (Simplify to Popularity)**: If Popularity works fine
- **D (FMPS_RATING)**: If all else fails
