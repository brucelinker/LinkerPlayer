using LinkerPlayer.Models;
using System.Collections.ObjectModel;
using File = TagLib.File;

namespace LinkerPlayer.ViewModels.Properties;

/// <summary>
/// Interface for loading metadata sections into TagItem collections
/// </summary>
public interface IMetadataLoader
{
    /// <summary>
    /// Load metadata from a single audio file
    /// </summary>
    /// <param name="audioFile">TagLib file to read from</param>
    /// <param name="targetCollection">Collection to populate with TagItems</param>
    void Load(File audioFile, ObservableCollection<TagItem> targetCollection);

    /// <summary>
    /// Load metadata from multiple audio files (for multi-selection support)
    /// </summary>
    /// <param name="audioFiles">List of TagLib files to read from</param>
    /// <param name="targetCollection">Collection to populate with TagItems (will show &lt;various&gt; for differing values)</param>
    void LoadMultiple(IReadOnlyList<File> audioFiles, ObservableCollection<TagItem> targetCollection);
}
