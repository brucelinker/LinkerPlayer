using ATL;
using LinkerPlayer.Models;
using System.Collections.ObjectModel;

namespace LinkerPlayer.ViewModels.Properties;

public interface IAtlMetadataLoader
{
    void Load(Track track, ObservableCollection<TagItem> targetCollection);
    void LoadMultiple(IReadOnlyList<Track> audioFiles, ObservableCollection<TagItem> targetCollection);
}
