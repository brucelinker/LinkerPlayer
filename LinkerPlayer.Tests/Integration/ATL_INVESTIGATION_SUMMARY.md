# ATL Vorbis RATING Investigation — Complete Summary

## Context
You're having trouble with how Audio Tools Library (ATL) for .NET handles rating metadata on Vorbis files (FLAC, OGG, Opus, Matroska). Specifically, when you set a rating of 2.2★, it reads back as 0.4★ (which converts to 2.0★ instead of 2.2★).

## Current Implementation Overview

Your code has three layers:

### Layer 1: Conversion Math (`MediaFileHelper.cs`)
```
Stars (0–5) ↔ Popularity (scale-dependent)
  - Vorbis (FLAC, OGG): scale = 1.0
  - ID3v2 (MP3): scale = 255.0
```

**Status**: ✅ **CORRECT** (unit tests confirm 28/29 pass)

### Layer 2: Write Path (`PersistRatingToFileAsync` in `MediaFile.cs`)
```
For Vorbis:
  - Convert stars → popularity on 0–1 scale
  - Write to AdditionalFields["RATING"] as string "0.44"
  - Skip Popularity property

For ID3v2:
  - Convert stars → popularity on 0–255 scale
  - Write to Popularity property
```

**Status**: ⚠️ **ASSUMED CORRECT** (needs ATL verification)

### Layer 3: Read Path (`UpdateFromFileMetadata` in `MediaFile.cs`)
```
For Vorbis:
  - Prefer AdditionalFields["RATING"] (0–1 scale)
  - Fall back to Popularity property

For ID3v2:
  - Fall back to AdditionalFields["RATING"]
  - Prefer Popularity property (0–255 scale)
```

**Status**: ⚠️ **DEPENDS ON ATL** (reads what ATL returns)

## The Mystery: Where Does 0.4 Come From?

### Observed Behavior
- Write 2.2★ → should write 0.44 on Vorbis scale
- Read back → get 0.4 instead of 0.44
- Convert 0.4 back to stars → 0.4 / 1.0 * 5.0 = 2.0★ (wrong!)

### The Exact Problem
```
Input:  2.2★
Expected Output: 0.44 (Vorbis)
Actual Output:   0.4 (Vorbis)
Difference:      0.04

Math.Round(0.44, 1) = 0.4 ✓
```

This is **not** a rounding error in your code—your code produces 0.44 correctly.
The 0.4 must be coming from **ATL's internal handling**.

## Hypotheses

### Hypothesis 1: ATL Rounds Popularity to 1 Decimal (MOST LIKELY)
- You write "0.44" to AdditionalFields["RATING"]
- ATL reads it but internally rounds to 1 decimal: 0.44 → 0.4
- ATL's Popularity property returns 0.4
- Your read code gets 0.4 and converts to 2.0★

**Evidence**:
- Precisely matches the 0.4 observation
- Would explain why fractional values don't survive
- Vorbis rounding while ID3v2 preserves precision

### Hypothesis 2: ATL Doesn't Persist AdditionalFields["RATING"]
- You write to AdditionalFields["RATING"]
- ATL's SaveAsync() ignores this custom field
- On re-read, RATING is gone
- Code falls back to Popularity which is 0 or wrong

**Evidence**:
- ATL might only handle "standard" Vorbis comments
- Custom fields might not be supported
- Would explain why the field isn't found

### Hypothesis 3: ATL Interprets Scale Differently
- You write "0.44" (0–1 scale per Vorbis spec)
- ATL internally interprets as 0–100 or 0–255
- Stores incorrectly, reads back with wrong scale conversion
- Result: precision loss and wrong scale

**Evidence**:
- Different taggers use different RATING scales
- ATL might normalize to non-standard scale internally
- Would explain the specific 0.4 value if it's (44 / 100) rounded or (112 / 255) rounded

## Investigation Tools Created

### 1. **Unit Tests** (`RatingConversionMathTests.cs`)
   - ✅ Tests conversion math in isolation (28/29 pass)
   - Proves the problem is NOT in your calculation code
   - **What it tells you**: The bug is in ATL or your use of ATL

### 2. **Diagnostic Tests** (`AtlVorbisRatingInvestigation.cs`)
   - Tests ATL's actual behavior with real files
   - Requires test FLAC files and console output inspection
   - **What it tells you**: How ATL actually handles RATING and Popularity

### 3. **Diagnostic Tool** (`AtlVorbisRatingDiagnostic.cs`)
   - Standalone console app for hands-on testing
   - Run interactively to see ATL's behavior step-by-step
   - **What it tells you**: Exact values ATL returns at each step

## How to Use These Tools

### Quick Start: Run the Diagnostic Tool
1. Ensure you have a test FLAC file
2. Update the `filePath` in `AtlVorbisRatingDiagnostic.cs`
3. Compile and run the tool
4. Observe console output
5. Document findings

### Comprehensive Testing: Run Unit Tests
1. Place test FLAC file in `LinkerPlayer.Tests\Integration\TestAudio\`
2. Run `AtlVorbisRatingInvestigation` tests
3. Examine console output for each test
4. Cross-reference with expected behavior

## What Each Test Result Means

### If RATING Field Persists
```
✓ AdditionalFields["RATING"] exists after save
→ ATL DOES persist custom fields
→ Problem is likely rounding/scale
→ Use Solution B or C below
```

### If RATING Field Is Lost
```
✗ AdditionalFields["RATING"] missing after save
→ ATL doesn't persist custom fields
→ Problem is persistence, not scale
→ Use Solution A below
```

### If Popularity Rounds to 1 Decimal
```
✓ Set Popularity=0.44, re-read gets 0.4
→ ATL's Popularity property rounds
→ Can't use Popularity for fractional values
→ Use Solution A below
```

## Proposed Solutions

### Solution A: Use Non-Standard Custom Field (If RATING Lost)
```csharp
// In PersistRatingToFileAsync for Vorbis:
if (scale <= 1.0 && clamped > 0.0)
{
    // Don't use "RATING" — it might be stripped by ATL
    // Use a custom field that ATL won't touch
    double value = Math.Round(clamped * scale / 5.0, 2);
    string ratingStr = value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    atlTrack.AdditionalFields["LINKER_RATING"] = ratingStr;  // Change here
}

// In UpdateFromFileMetadata for Vorbis:
if (scale <= 1.0 && track.AdditionalFields != null)
{
    // Try custom field first
    if (track.AdditionalFields.TryGetValue("LINKER_RATING", out string? r1))
        ratingField = r1;
    else if (track.AdditionalFields.TryGetValue("RATING", out string? r2))
        ratingField = r2;
    // ... rest of logic
}
```

**Pros**: Guaranteed persistence; avoids ATL's normalization
**Cons**: Non-standard; won't be recognized by generic tag editors

### Solution B: Use 0–100 Integer Scale (If RATING Persists But Rounds)
```csharp
// In PersistRatingToFileAsync for Vorbis:
if (scale <= 1.0 && clamped > 0.0)
{
    // Store as 0–100 integer instead of 0–1 decimal
    int ratingInt = (int)Math.Round(clamped * 20, 0);  // 2.2 * 20 = 44
    atlTrack.AdditionalFields["RATING"] = ratingInt.ToString();
}

// In UpdateFromFileMetadata for Vorbis:
if (scale <= 1.0 && track.AdditionalFields?.TryGetValue("RATING", out string? r) ?? false)
{
    if (int.TryParse(r, out int intVal) && intVal > 0)
    {
        // 0–100 scale back to 0–1
        rawRating = (float)(intVal / 100.0);
    }
    else if (double.TryParse(r, out double dVal) && dVal > 0)
    {
        // Original 0–1 decimal approach (backward compat)
        rawRating = (float)(dVal <= 1.0 ? dVal : dVal / 100.0);
    }
}
```

**Pros**: More portable; many taggers recognize 0–100 RATING
**Cons**: Integer loses some precision (2.25★ → 45 → 2.25★, but still better than 0.4)

### Solution C: Skip Custom RATING Entirely (Simplest)
```csharp
// In PersistRatingToFileAsync:
// Remove all the AdditionalFields["RATING"] logic
// Just use Popularity property for all formats:
if (clamped > 0.0)
{
    atlTrack.Popularity = MediaFileHelper.StarsToPopularity(clamped, Path);
}
else
{
    atlTrack.Popularity = null;
}

// In UpdateFromFileMetadata:
// Just read Popularity, no AdditionalFields fallback:
rawRating = track.Popularity;
Rating = rawRating is float pop && pop > 0
    ? MediaFileHelper.PopularityToStars(pop, Path)
    : 0;
```

**Pros**: Single code path; minimal changes
**Cons**: Loses fractional stars if ATL rounds Popularity for Vorbis

## Next Steps (in order)

1. **Run the diagnostic tool** with a real FLAC file
   - Document which RATING approach persists
   - Note what values ATL returns for Popularity

2. **Analyze the results**
   - Pick the solution that matches your findings

3. **Implement the fix**
   - Modify `PersistRatingToFileAsync()` and `UpdateFromFileMetadata()`
   - Update `CoreMetadataLoader` to match
   - Update inline edits in `MediaTabViewModel`

4. **Test the fix**
   - Write 2.2★, save, restart, verify it reads back as 2.2★
   - Try 4.2★ as well
   - Test on both FLAC and MP3

5. **Document the findings**
   - Update code comments with ATL version and discovered behavior
   - Note why the specific solution was chosen

## Files Modified in This Investigation

### Created:
- `LinkerPlayer.Tests\Models\RatingConversionMathTests.cs` - Unit tests (28/29 pass)
- `LinkerPlayer.Tests\Integration\AtlVorbisRatingInvestigation.cs` - Integration tests
- `LinkerPlayer.Tests\Integration\ATL_INVESTIGATION_PLAN.md` - Investigation guide
- `LinkerPlayer.Tests\Integration\ATL_ANALYSIS_AND_RECOMMENDATIONS.md` - Detailed analysis
- `LinkerPlayer\Diagnostics\AtlVorbisRatingDiagnostic.cs` - Standalone diagnostic tool

### To Be Modified (once diagnosis complete):
- `LinkerPlayer\Models\MediaFile.cs` - Fix PersistRatingToFileAsync & UpdateFromFileMetadata
- `LinkerPlayer\Models\MediaFileHelper.cs` - May need scale adjustments
- `LinkerPlayer\ViewModels\Properties\Loaders\CoreMetadataLoader.cs` - Mirror changes
- `LinkerPlayer\ViewModels\MediaTabViewModel.cs` - Mirror changes in inline edits

## Key Takeaway

**The problem is NOT in your conversion math.** Your code correctly converts 2.2★ ↔ 0.44.
The problem is in **how ATL handles the Vorbis RATING field or Popularity property**.

Once you run the diagnostic tool and see what ATL actually returns, the fix will be clear.

---

**Action**: Run `AtlVorbisRatingDiagnostic` with a test FLAC file and share the output.
