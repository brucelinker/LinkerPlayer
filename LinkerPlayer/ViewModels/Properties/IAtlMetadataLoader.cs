using System.Collections.ObjectModel;
using System.Collections.Generic;
using ATL;
using LinkerPlayer.Models;

namespace LinkerPlayer.ViewModels.Properties;

public interface IAtlMetadataLoader
{
    void Load(Track track, ObservableCollection<TagItem> targetCollection);
    void LoadMultiple(IReadOnlyList<Track> audioFiles, ObservableCollection<TagItem> targetCollection);
}
