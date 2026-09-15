# ATL Vorbis Rating Investigation — Complete Toolkit

## Overview

You reported an issue where setting a 2.2★ rating on a FLAC file (Vorbis) reads back as 0.4 (which converts to 2.0★). This toolkit helps you diagnose and fix the root cause.

## The Problem (Quick Summary)

- **Expected**: 2.2★ → write as 0.44 → read back as 2.2★
- **Actual**: 2.2★ → write as 0.44 → read back as 0.4 → converts to 2.0★
- **Root Cause**: Unknown — could be ATL rounding, scale confusion, or persistence issue

## Current State

### Code (Already Implemented)
✅ Conversion math is correct (unit tests: 28/29 pass)
✅ Write path exists (but may not persist correctly)
✅ Read path exists (but may read wrong value from ATL)

### Issue
⚠️ ATL's actual behavior with Vorbis RATING is unknown
⚠️ Don't know if AdditionalFields["RATING"] persists
⚠️ Don't know if Popularity property rounds values
⚠️ Don't know what scale ATL actually uses

## Investigation Toolkit

### Files Provided

#### 1. **RatingConversionMathTests.cs**
   - **What**: Unit tests for conversion math
   - **Status**: ✅ 28/29 tests pass
   - **Purpose**: Proves the bug is NOT in your conversion code
   - **How to use**: `dotnet test --filter TypeName=RatingConversionMathTests`
   - **Expects**: All tests pass

#### 2. **AtlVorbisRatingInvestigation.cs**
   - **What**: Integration tests using real ATL library
   - **Status**: Ready to run (requires test FLAC files)
   - **Purpose**: Test ATL's actual behavior with write/read cycles
   - **How to use**: 
     1. Place a test FLAC file in `LinkerPlayer.Tests\Integration\TestAudio\test.flac`
     2. Run: `dotnet test --filter TypeName=AtlVorbisRatingInvestigation`
     3. Read console output carefully
   - **Key tests**:
     - `VorbisRatingWriteRead_CheckPersistence` — Does RATING persist?
     - `VorbisPopularitySetter_CheckActualScale` — What scale does Popularity use?
     - `TwoPointTwoStarWrite_CheckWhichPersists` — Reproduces your exact problem

#### 3. **AtlVorbisRatingDiagnostic.cs**
   - **What**: Interactive diagnostic tool
   - **Status**: Ready to use (standalone method)
   - **Purpose**: Step-by-step hands-on testing
   - **How to use**:
     ```csharp
     // In a test or your own diagnostic code:
     AtlVorbisRatingDiagnostic.RunDiagnostic(@"D:\path\to\test.flac");
     ```
   - **What it does**:
     - Writes RATING in multiple formats
     - Reads back and logs the results
     - Tests alternative field names
     - Prints exactly what ATL returns
   - **Output**: Detailed console logs showing ATL's behavior

#### 4. **ATL_INVESTIGATION_SUMMARY.md**
   - **What**: Complete overview document
   - **Contains**:
     - Problem description
     - Hypothesis list
     - Expected outcomes for each test
     - Proposed solutions (A, B, C)
     - Next steps

#### 5. **ATL_ANALYSIS_AND_RECOMMENDATIONS.md**
   - **What**: Detailed technical analysis
   - **Contains**:
     - Hypothesis details with evidence
     - Investigation steps
     - Possible outcomes and fixes
     - Diagnostic code reference

#### 6. **ATL_INVESTIGATION_PLAN.md**
   - **What**: Structured investigation guide
   - **Contains**:
     - Test descriptions
     - Expected outcomes
     - Questions to answer
     - Direct file inspection instructions

## Quick Start (5 minutes)

### Step 1: Run Math Tests
```bash
cd LinkerPlayer.Tests
dotnet test --filter TypeName=RatingConversionMathTests
```

**Expected**: All tests pass ✓
**If failed**: There's a bug in conversion math (unlikely)

### Step 2: Prepare Test Audio
- Find any FLAC file on your computer
- Copy it to: `LinkerPlayer.Tests\Integration\TestAudio\test.flac`
- Create directory if it doesn't exist

### Step 3: Run Diagnostic Tool
Create a simple test file:

```csharp
// In any test, or create a new one:
[Fact]
public void RunDiagnostic()
{
    string testFlac = @"LinkerPlayer.Tests\Integration\TestAudio\test.flac";
    AtlVorbisRatingDiagnostic.RunDiagnostic(Path.GetFullPath(testFlac));
}
```

Run it and **copy all console output**.

### Step 4: Analyze Results
Compare actual output with expected behavior in `ATL_INVESTIGATION_SUMMARY.md`.

### Step 5: Pick a Solution
Based on results, choose Solution A, B, or C from the summary doc.

## Expected Diagnostic Outputs

### Scenario 1: RATING Persists, No Rounding
```
[WRITE] After write: RATING=0.44
[READ] Fresh read: RATING=0.44
[READ] Converts to 2.2★  ✓
```
**Implication**: ATL respects RATING field. No fix needed (problem elsewhere).

### Scenario 2: RATING Persists, Rounds to 0.4
```
[WRITE] After write: RATING=0.44
[READ] Fresh read: RATING=0.4  ← ROUNDED!
[READ] Converts to 2.0★  ✗
```
**Implication**: Use Solution B (0–100 integer) or A (different field name).

### Scenario 3: RATING Lost After Save
```
[WRITE] After write: RATING=0.44
[READ] Fresh read: RATING=NOT FOUND
[READ] Falls back to Popularity=null  ✗
```
**Implication**: Use Solution A (custom field name like "LINKER_RATING").

### Scenario 4: Popularity Returns Wrong Scale
```
[POPULARITY-WRITE] Set Popularity=0.44f
[POPULARITY-READ] After re-read: Popularity=0.4  ← ROUNDED!
```
**Implication**: Popularity isn't reliable for Vorbis. Use Solution A or B.

## Solutions At a Glance

| Solution | When to Use | Pros | Cons |
|----------|-------------|------|------|
| **A** (Custom field) | RATING doesn't persist | Guaranteed to work | Non-standard |
| **B** (0–100 integer) | RATING persists but loses precision | Portable | Integer only |
| **C** (Simplify) | Popularity works fine | Minimal changes | Fractional values may be lost |

## After Diagnosis: Implementation Guide

### Step 1: Confirm Your Diagnosis
- Note which scenario matched your diagnostic output
- Confirm that scenario doesn't occur in another test

### Step 2: Implement the Fix
Modify these files (find exact locations in code comments):
- `LinkerPlayer\Models\MediaFile.cs` → `PersistRatingToFileAsync()` & `UpdateFromFileMetadata()`
- `LinkerPlayer\ViewModels\Properties\Loaders\CoreMetadataLoader.cs` → Rating read
- `LinkerPlayer\ViewModels\MediaTabViewModel.cs` → Inline edit rating write

### Step 3: Test the Fix
```bash
# Write 2.2★ to a test FLAC
# Close and reopen the app (or refresh library)
# Verify it still shows 2.2★, not 2.0★

# Test on both FLAC and MP3
```

### Step 4: Verify No Regressions
```bash
dotnet test --filter "Rating"
```

## Troubleshooting

### "Test file not found"
- Ensure `LinkerPlayer.Tests\Integration\TestAudio\test.flac` exists
- If not, create the directory and copy any FLAC file there

### "Test hangs or times out"
- ATL file I/O might be slow on network drives
- Use a local file, not UNC path

### "Diagnostic shows nothing"
- Check that console output is being captured
- Run diagnostic from a test with proper test framework output

### "I don't understand the output"
- Compare line-by-line with "Expected Diagnostic Outputs" above
- Document the mismatch and ask for clarification

## Key Files in This Investigation

```
LinkerPlayer.Tests/
├── Models/
│   └── RatingConversionMathTests.cs          ← Unit tests (math validation)
├── Integration/
│   ├── AtlVorbisRatingInvestigation.cs       ← Integration tests (ATL behavior)
│   ├── ATL_INVESTIGATION_SUMMARY.md          ← Overview
│   ├── ATL_ANALYSIS_AND_RECOMMENDATIONS.md   ← Detailed analysis
│   ├── ATL_INVESTIGATION_PLAN.md             ← Step-by-step guide
│   └── TestAudio/
│       └── test.flac                          ← (You must provide this)

LinkerPlayer/
├── Models/
│   ├── MediaFile.cs                          ← To be modified
│   └── MediaFileHelper.cs                    ← Already correct (don't change)
├── Diagnostics/
│   └── AtlVorbisRatingDiagnostic.cs          ← Diagnostic tool
└── ViewModels/
    ├── MediaTabViewModel.cs                  ← May need modification
    └── Properties/
        └── Loaders/
            └── CoreMetadataLoader.cs         ← May need modification
```

## Timeline

1. **Now**: You're here — reading this file
2. **Next (5 min)**: Run math tests to confirm conversion code is correct
3. **Next (10 min)**: Prepare test FLAC file
4. **Next (5 min)**: Run diagnostic tool, capture output
5. **Next (20 min)**: Analyze output, pick solution
6. **Next (20 min)**: Implement fix
7. **Next (10 min)**: Test and verify

**Total**: ~70 minutes from start to fix

## Questions?

Refer to:
- **"Why this problem?"** → ATL_INVESTIGATION_SUMMARY.md
- **"How to test?"** → ATL_INVESTIGATION_PLAN.md
- **"Which solution?"** → ATL_ANALYSIS_AND_RECOMMENDATIONS.md
- **"How to implement?"** → Solution section in ATL_ANALYSIS_AND_RECOMMENDATIONS.md

---

**Status**: Ready for you to run the diagnostic tool and gather data.
**Next**: Follow Step 1 above (Run Math Tests).
