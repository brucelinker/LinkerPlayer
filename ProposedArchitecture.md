# Proposed Simplified PropertiesViewModel Architecture

## Core Principle: **Explicit > Implicit**

Instead of discovering and filtering fields, explicitly define what we want in each section.

## Architecture Changes:

### 1. **Separate Loaders by Responsibility**
```csharp
private void LoadCoreMetadata(Tag tag)        // Explicit standard fields only
private void LoadCustomMetadata(Tag tag)      // Format-specific custom fields  
private void LoadFileProperties()             // File technical info
private void LoadReplayGainData(Tag tag)      // ReplayGain only
private void LoadPictureData(Tag tag)         // Picture info only
private void LoadLyricsData(Tag tag)          // Lyrics only (future)
```

### 2. **Explicit Field Definitions**
```csharp
// No more discovery/filtering - just explicit lists
private readonly string[] _coreMetadataFields = {
    "Title", "Artist", "Album", "Album Artist", "Year", "Genre", 
    "Composer", "Track Number", "Total Tracks", "Disc Number", 
    "Total Discs", "Comment", "Copyright", "Beats Per Minute",
    "Conductor", "Grouping", "Publisher", "ISRC"
};

private readonly string[] _filePropertyFields = {
    "Duration", "Bitrate", "Sample Rate", "Channels", 
    "Tag Type", "ID3v2 Version", "Media Types", "Description", 
    "Codec", "Bits Per Sample"
};
```

### 3. **Smart Field Handlers**
```csharp
private abstract class FieldHandler
{
    public abstract string GetValue(Tag tag);
    public abstract void SetValue(Tag tag, string value);
}

private class ArtistFieldHandler : FieldHandler
{
    public override string GetValue(Tag tag) => GetBestArtistField(tag);
    public override void SetValue(Tag tag, string value) => tag.Performers = ParseArray(value);
}
```

### 4. **Clean Section Interfaces**
```csharp
public interface IMetadataSection
{
    void LoadData(File audioFile);
    void ApplyChanges();
    ObservableCollection<TagItem> Items { get; }
}

public class CoreMetadataSection : IMetadataSection { ... }
public class CustomMetadataSection : IMetadataSection { ... }
public class FilePropertiesSection : IMetadataSection { ... }
public class ReplayGainSection : IMetadataSection { ... }
```

## Benefits:

1. **Explicit Control**: We decide exactly what appears where
2. **No Filter Arrays**: No need for complex redundancy filtering
3. **Separated Concerns**: Each section handles its own data
4. **Testable**: Each section can be unit tested independently
5. **Extensible**: Easy to add new sections (Lyrics, etc.)
6. **Maintainable**: Much smaller, focused methods
7. **Predictable**: No surprises about what fields appear

## Custom Tags Strategy:

For custom tags, have a dedicated section that:
- Only shows non-standard fields
- Uses format-specific discovery (Vorbis comments, MP4 iTunes fields, etc.)
- Clearly labeled as "Custom Fields" or "Advanced Metadata"

## File Size Reduction:

- Main ViewModel: ~300-400 lines (just coordination)
- Each section: ~100-150 lines
- Field handlers: ~20-50 lines each
- Total: Similar line count but much better organized

Would you like me to start implementing this cleaner architecture?