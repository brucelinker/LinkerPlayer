using System.Collections.ObjectModel;
using LinkerPlayer.Models;

namespace LinkerPlayer.ViewModels.Properties;

/// <summary>
/// Legacy interface for TagLib-based metadata loaders. Keep for backward compatibility.
/// New ATL-based loaders should implement IAtlMetadataLoader instead.
/// </summary>
public interface IMetadataLoader
{
    void Load(object audioFile, ObservableCollection<TagItem> targetCollection);
    void LoadMultiple(IReadOnlyList<object> audioFiles, ObservableCollection<TagItem> targetCollection);
}
