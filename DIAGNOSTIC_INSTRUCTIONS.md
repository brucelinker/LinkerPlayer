# Next Steps - Run the Diagnostic

## ✅ Step 1 Complete: Math Tests All Pass (29/29)

Your rating conversion code is 100% correct!

---

## 📋 Step 2-3: Run the Diagnostic

### What You Need

1. **A FLAC audio file** (any FLAC file will work)
   - From your music library, Windows samples, or download one
   - Example: `C:\Music\SomeSong.flac`

### How to Run the Diagnostic

**Option A: Use Test Explorer (Easiest)**

1. In Visual Studio, open **Test Explorer** (Test → Test Explorer)
2. Search for: `ExecuteDiagnostic`
3. You'll see the test (currently skipped)
4. Edit file: `LinkerPlayer.Tests\Integration\RunDiagnostic.cs`
5. Remove the `[Fact(Skip = "...")]` attribute
6. Change it to just: `[Fact]`
7. Update the diagnostic method to use your FLAC file path
8. Click **Run** → Watch Output window for results

**Option B: Call it Directly**

```csharp
// Add this to any test class or Main() method:
using LinkerPlayer.Diagnostics;

// Run the diagnostic on your FLAC file
AtlVorbisRatingDiagnostic.RunDiagnostic(@"C:\Music\YourFile.flac");
```

**Option C: Quick Test Method**

```csharp
// In LinkerPlayer.Tests\Integration\RunDiagnostic.cs, add:
[Fact]
public void DiagnosticWithYourFile()
{
    AtlVorbisRatingDiagnostic.RunDiagnostic(@"C:\Music\YourFile.flac");
}
```

Then run this test.

---

## 📊 What the Diagnostic Does

It will output something like:

```
=== ATL Vorbis RATING Diagnostic ===
Test File: C:\Music\YourFile.flac

--- TEST 1: Read Initial State ---
Initial Popularity: null

--- TEST 2: Write RATING via AdditionalFields ---
Writing AdditionalFields["RATING"] = "0.44"
SaveAsync returned: True
After save (in-memory):
  Popularity: null
  AdditionalFields["RATING"]: 0.44

--- TEST 3: Re-read Fresh from Disk ---
Fresh read Popularity: 0.4
Rating-related AdditionalFields:
  RATING = 0.44
```

---

## 🎯 What to Look For

### Scenario A: RATING is Lost
```
--- TEST 3: Re-read Fresh from Disk ---
Fresh read Popularity: null
Rating-related AdditionalFields:
  (none found)
```
**Action**: Use **Solution A** (custom field name)

### Scenario B: RATING Persists But Rounds
```
--- TEST 3: Re-read Fresh from Disk ---
Fresh read Popularity: 0.4        ← ROUNDED! Should be 0.44
Rating-related AdditionalFields:
  RATING = 0.44                   ← Stored correctly
```
**Action**: Use **Solution B** (0–100 integer)

### Scenario C: Everything Works Fine
```
--- TEST 3: Re-read Fresh from Disk ---
Fresh read Popularity: 0.44       ← Correct!
Rating-related AdditionalFields:
  RATING = 0.44
```
**Action**: Use **Solution C** (simplify, no changes needed)

---

## 📌 After You Get Results

1. **Copy the diagnostic output** from the test results
2. **Match it to Scenario A, B, or C** above
3. **Pick the corresponding solution** from the guide
4. **Implement the fix** (5-20 line change)
5. **Test** that 2.2★ reads back correctly

---

## 📚 Reference Guides

All in `LinkerPlayer.Tests\Integration\`:
- `README.md` — Complete quick start
- `ATL_INVESTIGATION_SUMMARY.md` — Full analysis + solutions
- `ATL_ANALYSIS_AND_RECOMMENDATIONS.md` — Detailed technical breakdown

---

**Ready?** Pick a FLAC file, run the diagnostic, and share the output!
